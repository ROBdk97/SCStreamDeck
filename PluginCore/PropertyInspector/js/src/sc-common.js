//// ****************************************************************
// * SC Common Utilities
// * Shared utilities for Star Citizen StreamDeck Plugin
//// ****************************************************************

(function () {
  const root = globalThis;
  const SCPI = root.SCPI = root.SCPI || {};
  const util = SCPI.util = SCPI.util || {};

  function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
    };
  }

  function onDocumentReady(callback) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', callback);
    } else {
      callback();
    }
  }

  function sendToPlugin(event, payload = {}) {
    try {
      if (!root.SDPIComponents?.streamDeckClient?.send) {
        console.warn('[sc-common] SDPIComponents.streamDeckClient.send not available');
        return;
      }
      root.SDPIComponents.streamDeckClient.send('sendToPlugin', {event, ...payload});
    } catch (err) {
      console.error('[sc-common] sendToPlugin failed', err);
    }
  }

  util.debounce = debounce;
  util.onDocumentReady = onDocumentReady;
  util.sendToPlugin = sendToPlugin;

  if (typeof root.debounce !== 'function') {
    root.debounce = debounce;
  }
  if (typeof root.onDocumentReady !== 'function') {
    root.onDocumentReady = onDocumentReady;
  }
  if (typeof root.sendToPlugin !== 'function') {
    root.sendToPlugin = sendToPlugin;
  }
})();

(function () {
  const root = globalThis;
  const SCPI = root.SCPI = root.SCPI || {};
  const listeners = new Set();
  const supportedLocales = new Set(['en', 'de', 'fr', 'es']);
  const localeBundles = new Map();
  const selector = [
    '[data-i18n-text]',
    '[data-i18n-html]',
    '[data-i18n-title]',
    '[data-i18n-placeholder]',
    '[data-i18n-aria-label]',
    '[data-i18n-value]'
  ].join(',');

  let initialized = false;
  let busBound = false;
  let localeRootUrl = '';
  let currentLocale = 'en';
  let currentTranslations = {};
  let currentLocaleInfo = {mode: 'auto', override: null, detected: null, effective: 'en'};
  let hasLoadedTranslations = false;
  let loadToken = 0;

  function normalizeLocale(locale) {
    if (typeof locale !== 'string' || locale.trim().length === 0) {
      return null;
    }

    const normalized = locale.trim().replace(/_/g, '-').toLowerCase();
    if (supportedLocales.has(normalized)) {
      return normalized;
    }

    const [primary] = normalized.split('-', 1);
    return supportedLocales.has(primary) ? primary : null;
  }

  function resolveLocaleRootUrl(explicitUrl) {
    const baseHref = root.location?.href || document.baseURI;

    try {
      if (typeof explicitUrl === 'string' && explicitUrl.trim().length > 0) {
        return new URL(explicitUrl, baseHref).href;
      }

      return new URL('../../', baseHref).href;
    } catch (_) {
      return '';
    }
  }

  function isPlainObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
  }

  function deepMerge(base, overlay) {
    if (!isPlainObject(base)) {
      return isPlainObject(overlay) ? deepMerge({}, overlay) : overlay;
    }

    const merged = {...base};
    if (!isPlainObject(overlay)) {
      return merged;
    }

    Object.entries(overlay).forEach(([key, value]) => {
      if (isPlainObject(value) && isPlainObject(merged[key])) {
        merged[key] = deepMerge(merged[key], value);
        return;
      }

      merged[key] = isPlainObject(value) ? deepMerge({}, value) : value;
    });

    return merged;
  }

  function getValueByPath(source, path) {
    if (!source || typeof path !== 'string' || path.trim().length === 0) {
      return undefined;
    }

    return path.split('.').reduce((value, segment) => {
      if (value === null || value === undefined) {
        return undefined;
      }

      return value[segment];
    }, source);
  }

  function applyReplacements(text, replacements) {
    const template = typeof text === 'string' ? text : '';
    if (!replacements || typeof replacements !== 'object') {
      return template;
    }

    return template.replace(/\{([^}]+)\}/g, (match, token) => {
      const value = replacements[token];
      return value === undefined || value === null ? match : String(value);
    });
  }

  async function loadBundle(locale) {
    const normalizedLocale = normalizeLocale(locale) || 'en';
    if (localeBundles.has(normalizedLocale)) {
      return localeBundles.get(normalizedLocale);
    }

    const promise = (async () => {
      const rootUrl = localeRootUrl || resolveLocaleRootUrl();
      const baseHref = root.location?.href || document.baseURI;

      try {
        const response = await fetch(new URL(`${normalizedLocale}.json`, rootUrl || baseHref).href, {
          cache: 'no-store'
        });

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const json = await response.json();
        return isPlainObject(json?.Localization) ? json.Localization : {};
      } catch (err) {
        console.warn(`[sc-i18n] Failed to load locale "${normalizedLocale}"`, err);
        return {};
      }
    })();

    localeBundles.set(normalizedLocale, promise);
    return promise;
  }

  function getFallbackForAttribute(element, attributeName) {
    const explicit = element.getAttribute(`data-i18n-${attributeName}-fallback`);
    if (explicit !== null) {
      return explicit;
    }

    switch (attributeName) {
      case 'text':
        return element.textContent || '';
      case 'html':
        return element.innerHTML || '';
      case 'title':
        return element.getAttribute('title') || '';
      case 'placeholder':
        return element.getAttribute('placeholder') || '';
      case 'aria-label':
        return element.getAttribute('aria-label') || '';
      case 'value':
        return element.value || '';
      default:
        return '';
    }
  }

  function translateElement(element) {
    if (!element || typeof element.getAttribute !== 'function') {
      return;
    }

    const textKey = element.getAttribute('data-i18n-text');
    if (textKey) {
      element.textContent = t(textKey, getFallbackForAttribute(element, 'text'));
    }

    const htmlKey = element.getAttribute('data-i18n-html');
    if (htmlKey) {
      element.innerHTML = t(htmlKey, getFallbackForAttribute(element, 'html'));
    }

    const titleKey = element.getAttribute('data-i18n-title');
    if (titleKey) {
      element.setAttribute('title', t(titleKey, getFallbackForAttribute(element, 'title')));
    }

    const placeholderKey = element.getAttribute('data-i18n-placeholder');
    if (placeholderKey) {
      element.setAttribute('placeholder', t(placeholderKey, getFallbackForAttribute(element, 'placeholder')));
    }

    const ariaLabelKey = element.getAttribute('data-i18n-aria-label');
    if (ariaLabelKey) {
      element.setAttribute('aria-label', t(ariaLabelKey, getFallbackForAttribute(element, 'aria-label')));
    }

    const valueKey = element.getAttribute('data-i18n-value');
    if (valueKey && 'value' in element) {
      element.value = t(valueKey, getFallbackForAttribute(element, 'value'));
    }
  }

  function applyTranslations(target = document) {
    if (!target) {
      return;
    }

    const nodes = [];
    if (typeof target.getAttribute === 'function') {
      nodes.push(target);
    }

    if (typeof target.querySelectorAll === 'function') {
      nodes.push(...target.querySelectorAll(selector));
    }

    nodes.forEach((node) => translateElement(node));
  }

  function notifyListeners() {
    const snapshot = {...currentLocaleInfo, effective: currentLocale};
    listeners.forEach((listener) => {
      try {
        listener(snapshot);
      } catch (err) {
        console.error('[sc-i18n] listener failed', err);
      }
    });
  }

  async function loadLocale(locale) {
    const nextLocale = normalizeLocale(locale) || 'en';
    const token = ++loadToken;

    if (hasLoadedTranslations && nextLocale === currentLocale) {
      document.documentElement.lang = currentLocale;
      applyTranslations(document);
      notifyListeners();
      return currentLocale;
    }

    const [english, localized] = await Promise.all([
      loadBundle('en'),
      nextLocale === 'en' ? Promise.resolve({}) : loadBundle(nextLocale)
    ]);

    if (token !== loadToken) {
      return currentLocale;
    }

    currentTranslations = deepMerge(deepMerge({}, english), localized);
    currentLocale = nextLocale;
    hasLoadedTranslations = true;
    document.documentElement.lang = currentLocale;
    applyTranslations(document);
    notifyListeners();
    return currentLocale;
  }

  function extractLocaleInfo(payload) {
    const candidate = payload?.pluginLocale || payload?.controlPanel?.pluginLocale || null;
    if (!candidate || typeof candidate !== 'object') {
      return null;
    }

    return {
      mode: String(candidate.mode || 'auto'),
      override: candidate.override ?? null,
      detected: candidate.detected ?? null,
      effective: normalizeLocale(candidate.effective) || 'en'
    };
  }

  function bindBus() {
    if (busBound) {
      return;
    }

    const attach = () => {
      if (busBound) {
        return;
      }

      if (typeof SCPI.bus?.on !== 'function') {
        setTimeout(attach, 0);
        return;
      }

      busBound = true;
      SCPI.bus.on((payload) => {
        const localeInfo = extractLocaleInfo(payload);
        if (!localeInfo) {
          return;
        }

        setLocaleInfo(localeInfo);
      });
    };

    attach();
  }

  function init(options = {}) {
    if (typeof options.localeRootUrl === 'string' && options.localeRootUrl.trim().length > 0) {
      localeRootUrl = resolveLocaleRootUrl(options.localeRootUrl);
    } else if (!localeRootUrl) {
      localeRootUrl = resolveLocaleRootUrl();
    }

    if (!initialized) {
      initialized = true;
      bindBus();
      return loadLocale(currentLocale);
    }

    if (!busBound) {
      bindBus();
    }

    return Promise.resolve(currentLocale);
  }

  function t(key, fallback = '', replacements = null) {
    const value = getValueByPath(currentTranslations, key);
    const text = typeof value === 'string' ? value : String(fallback ?? '');
    return applyReplacements(text, replacements);
  }

  function resolveSpec(spec, fallback = '') {
    if (typeof spec === 'string') {
      return spec;
    }

    if (isPlainObject(spec)) {
      return t(spec.key || '', spec.fallback ?? fallback, spec.replacements ?? null);
    }

    return String(fallback ?? '');
  }

  function setLocaleInfo(localeInfo) {
    const nextLocaleInfo = typeof localeInfo === 'string'
      ? {mode: 'auto', override: null, detected: null, effective: normalizeLocale(localeInfo) || 'en'}
      : {
        mode: String(localeInfo?.mode || currentLocaleInfo.mode || 'auto'),
        override: localeInfo?.override ?? currentLocaleInfo.override ?? null,
        detected: localeInfo?.detected ?? currentLocaleInfo.detected ?? null,
        effective: normalizeLocale(localeInfo?.effective) || 'en'
      };

    currentLocaleInfo = nextLocaleInfo;
    return loadLocale(nextLocaleInfo.effective);
  }

  function onChange(listener) {
    if (typeof listener !== 'function') {
      return () => {
      };
    }

    listeners.add(listener);
    if (hasLoadedTranslations) {
      listener({...currentLocaleInfo, effective: currentLocale});
    }

    return () => listeners.delete(listener);
  }

  SCPI.i18n = {
    init,
    bindBus,
    onChange,
    t,
    apply: applyTranslations,
    normalizeLocale,
    resolveSpec,
    setLocaleInfo,
    getLocaleInfo: () => ({...currentLocaleInfo, effective: currentLocale})
  };
})();

(function () {
  const root = globalThis;
  const SCPI = root.SCPI = root.SCPI || {};
  const functionPicker = SCPI.functionPicker = SCPI.functionPicker || {};

  function flattenFunctionsData(groups, options = {}) {
    const flat = [];
    const requireToggleCandidates = options.requireToggleCandidates === true;
    const excluded = new Set((options.excludeBindingTypes || []).map((t) => String(t || '').toLowerCase()));

    if (!Array.isArray(groups)) {
      return flat;
    }

    groups.forEach((group) => {
      const groupName = group?.label || 'Other';
      const entries = Array.isArray(group?.options) ? group.options : [];

      entries.forEach((opt) => {
        const bindingType = String(opt?.bindingType || '').toLowerCase();

        if (requireToggleCandidates) {
          const isToggleCandidate = opt && opt.details && opt.details.isToggleCandidate === true;
          if (!isToggleCandidate) {
            return;
          }
        }

        if (excluded.has(bindingType)) {
          return;
        }

        const disabledReason = String(opt?.disabledReason || '');
        const isUnbound = bindingType === 'unbound';
        const isDisabled = !!opt?.disabled && !isUnbound;

        flat.push({
          value: opt?.value,
          legacyValue: opt?.legacyValue,
          text: opt?.text,
          group: groupName,
          details: opt?.details,
          bindingType,
          disabledReason,
          unbound: isUnbound,
          disabled: isDisabled
        });
      });
    });

    return flat;
  }

  function updateFunctionDetails(options = {}) {
    const containerEl = options.containerEl || null;
    const titleEl = options.titleEl || null;
    const descriptionEl = options.descriptionEl || null;
    const bindingElements = options.bindingElements || {};
    const hideContainerWhenEmpty = options.hideContainerWhenEmpty === true;
    const showContainerWhenFilled = options.showContainerWhenFilled === true;
    const selectedOption = options.selectedOption || null;
    const slotLabel = String(options.slotLabel || '').trim();
    const i18n = SCPI?.i18n;

    const devices = ['keyboard', 'mouse', 'gamepad', 'joystick'];

    if (!selectedOption || !selectedOption.details) {
      if (hideContainerWhenEmpty && containerEl) {
        containerEl.style.display = 'none';
      }

      if (titleEl) {
        titleEl.textContent = '';
      }

      if (descriptionEl) {
        descriptionEl.textContent = '';
      }

      devices.forEach((device) => {
        const el = bindingElements[device] || null;
        if (el) {
          el.textContent = '';
        }
      });

      return;
    }

    if (showContainerWhenFilled && containerEl) {
      containerEl.style.display = 'block';
    }

    const details = selectedOption.details;
    const title = String(details.label || selectedOption.text || '');
    const description = String(details.description || '');
    const detailDevices = Array.isArray(details.devices) ? details.devices : [];
    const unboundText = i18n?.t('PropertyInspector.Common.State.Unbound', 'Unbound') || 'Unbound';
    const noDescriptionText = i18n?.t(
      'PropertyInspector.Common.State.NoDescription',
      'No description available.'
    ) || 'No description available.';

    if (titleEl) {
      titleEl.textContent = slotLabel.length > 0 ? `${slotLabel}: ${title}` : title;
    }

    devices.forEach((deviceType) => {
      const deviceData = detailDevices.find((d) => d.device && d.device.toLowerCase() === deviceType);
      const bindingEl = bindingElements[deviceType] || null;

      let bindingValue = unboundText;
      if (deviceData && Array.isArray(deviceData.bindings) && deviceData.bindings.length > 0) {
        const bindingLines = deviceData.bindings
          .map((binding) => String(binding.display || binding.raw || ''))
          .filter((text) => text);

        if (bindingLines.length > 0) {
          bindingValue = bindingLines.join(', ');
        }
      }

      if (bindingEl) {
        bindingEl.textContent = bindingValue;
      }
    });

    if (descriptionEl) {
      descriptionEl.textContent = description || noDescriptionText;
    }
  }

  functionPicker.flattenFunctionsData = flattenFunctionsData;
  functionPicker.updateFunctionDetails = updateFunctionDetails;
})();
