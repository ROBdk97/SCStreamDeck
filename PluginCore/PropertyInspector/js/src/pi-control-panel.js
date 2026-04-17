//// ****************************************************************
// * Control Panel PI Entrypoint
//// ****************************************************************

(function () {
  const SCPI = globalThis.SCPI;
  const CHANNELS = ['Live', 'Hotfix', 'Ptu', 'Eptu', 'TechPreview'];
  const CHANNEL_LABELS = {
    Live: 'LIVE',
    Hotfix: 'HOTFIX',
    Ptu: 'PTU',
    Eptu: 'EPTU',
    TechPreview: 'TECH-PREVIEW'
  };
  SCPI?.i18n?.init?.();
  SCPI?.i18n?.apply?.();
  const AUTO_PLUGIN_LOCALE = 'auto';
  const OVERRIDE_PLUGIN_LOCALE = 'override';
  const SUPPORTED_PLUGIN_LOCALES = ['en', 'de', 'fr', 'es'];
  const PLUGIN_LOCALE_LABELS = {
    en: 'English',
    de: 'German',
    fr: 'French',
    es: 'Spanish'
  };

  function t(key, fallback, replacements) {
    return SCPI?.i18n?.t?.(key, fallback, replacements) || fallback;
  }

  const getChannelLabel = (channel) => CHANNEL_LABELS[channel] || String(channel || '').toUpperCase();

  function normalizePluginLocaleMode(mode) {
    return String(mode || '').toLowerCase() === OVERRIDE_PLUGIN_LOCALE
      ? OVERRIDE_PLUGIN_LOCALE
      : AUTO_PLUGIN_LOCALE;
  }

  function normalizePluginLocaleValue(locale) {
    const text = String(locale || '').trim().toLowerCase();
    if (!text) {
      return null;
    }

    if (SUPPORTED_PLUGIN_LOCALES.includes(text)) {
      return text;
    }

    const [head] = text.split('-');
    return SUPPORTED_PLUGIN_LOCALES.includes(head) ? head : null;
  }

  function createDefaultPluginLocaleState() {
    return {
      mode: AUTO_PLUGIN_LOCALE,
      override: null,
      detected: null,
      effective: SUPPORTED_PLUGIN_LOCALES[0],
      supported: [...SUPPORTED_PLUGIN_LOCALES]
    };
  }

  function resolvePluginLocaleEffective(state) {
    if (state.mode === OVERRIDE_PLUGIN_LOCALE && state.override) {
      return state.override;
    }

    return state.detected || SUPPORTED_PLUGIN_LOCALES[0];
  }

  function normalizePluginLocaleState(data) {
    const base = createDefaultPluginLocaleState();
    const mode = normalizePluginLocaleMode(data?.mode);
    const overrideLocale = normalizePluginLocaleValue(data?.override);
    const detectedLocale = normalizePluginLocaleValue(data?.detected);
    const supportedValues = Array.isArray(data?.supported)
      ? data.supported.map((locale) => normalizePluginLocaleValue(locale)).filter(Boolean)
      : [];
    const effectiveLocale = normalizePluginLocaleValue(data?.effective);

    return {
      mode,
      override: overrideLocale,
      detected: detectedLocale,
      effective: effectiveLocale || resolvePluginLocaleEffective({
        mode,
        override: overrideLocale,
        detected: detectedLocale
      }),
      supported: supportedValues.length > 0 ? [...new Set(supportedValues)] : base.supported
    };
  }

  function formatPluginLocale(locale) {
    const normalized = normalizePluginLocaleValue(locale);
    if (!normalized) {
      return t('PropertyInspector.ControlPanel.PluginLocale.Unavailable', 'Unavailable');
    }

    const label = t(
      `PropertyInspector.ControlPanel.PluginLocale.Language.${normalized}`,
      PLUGIN_LOCALE_LABELS[normalized] || normalized.toUpperCase()
    );
    return `${label} (${normalized})`;
  }

  function buildPluginLocaleItems(state) {
    const autoTarget = state.detected
      ? t('PropertyInspector.ControlPanel.PluginLocale.AutoDetectedTarget', 'Detected: {locale}', {
        locale: formatPluginLocale(state.detected)
      })
      : t('PropertyInspector.ControlPanel.PluginLocale.AutoDefaultTarget', 'Default: {locale}', {
        locale: formatPluginLocale(state.effective)
      });
    const items = [{
      value: AUTO_PLUGIN_LOCALE,
      text: t('PropertyInspector.ControlPanel.PluginLocale.AutoOption', 'Auto ({target})', {target: autoTarget})
    }];

    for (const locale of SUPPORTED_PLUGIN_LOCALES) {
      items.push({
        value: locale,
        text: t(
          `PropertyInspector.ControlPanel.PluginLocale.Language.${locale}`,
          PLUGIN_LOCALE_LABELS[locale] || locale.toUpperCase()
        )
      });
    }

    return items;
  }

  function getPluginLocaleSelectionValue(state) {
    return state.mode === OVERRIDE_PLUGIN_LOCALE && state.override
      ? state.override
      : AUTO_PLUGIN_LOCALE;
  }

  function setText(id, value) {
    const element = document.getElementById(id);
    if (!element) {
      return;
    }

    element.textContent = String(value || '');
  }

  function updatePluginLocaleStatus(state) {
    setText(
      'pluginLocaleMode',
      state.mode === OVERRIDE_PLUGIN_LOCALE
        ? t('PropertyInspector.ControlPanel.PluginLocale.ModeOverride', 'Manual override')
        : t('PropertyInspector.ControlPanel.PluginLocale.ModeAuto', 'Auto-detected')
    );
    setText(
      'pluginLocaleOverride',
      state.override
        ? formatPluginLocale(state.override)
        : t('PropertyInspector.ControlPanel.PluginLocale.None', 'None')
    );
    setText(
      'pluginLocaleDetected',
      state.detected
        ? formatPluginLocale(state.detected)
        : t('PropertyInspector.ControlPanel.PluginLocale.Unavailable', 'Unavailable')
    );
    setText('pluginLocaleEffective', formatPluginLocale(state.effective));
  }

  function buildChannelItems(cp) {
    const channelMap = new Map();
    const arr = Array.isArray(cp?.channels) ? cp.channels : [];
    for (const c of arr) {
      if (c && typeof c.channel === 'string') {
        channelMap.set(c.channel, c);
      }
    }

    const items = [];
    for (const ch of CHANNELS) {
      const info = channelMap.get(ch);
      const valid = !!info?.valid;
      if (!valid) {
        continue;
      }

      const custom = !!info?.isCustomPath;
        items.push({
          value: ch,
          text: `${getChannelLabel(ch)} - ${custom
            ? t('PropertyInspector.ControlPanel.ChannelSuffixCustom', 'Custom')
            : t('PropertyInspector.ControlPanel.ChannelSuffixAuto', 'Auto')}`
        });
    }

    return items;
  }

  function setInlineError(elementId, text) {
    const el = document.getElementById(elementId);
    if (!el) {
      return;
    }

    const frame = el.parentElement && el.parentElement.classList.contains('pi-inline-banner')
      ? el.parentElement
      : null;

    const msg = String(text || '').trim();
    if (msg.length === 0) {
      el.textContent = '';
      el.style.display = 'none';
      if (frame) {
        frame.style.display = 'none';
      }
      return;
    }

    el.textContent = msg;
    el.style.display = 'block';
    if (frame) {
      frame.style.display = 'flex';
    }
  }

  function updateInstallWarning(cp) {
    const list = Array.isArray(cp?.channels) ? cp.channels : [];
    const anyConfigured = list.some((r) => !!r?.configured);
    const anyValid = list.some((r) => !!r?.valid);

    if (anyValid) {
      setInlineError('pi-install-warning', '');
      return;
    }

    if (!anyConfigured) {
      setInlineError(
        'pi-install-warning',
        t('PropertyInspector.Common.Status.NoInstallationDetected', 'No installation detected. Set custom path.')
      );
      return;
    }

    setInlineError(
      'pi-install-warning',
      t('PropertyInspector.Common.Status.NoInstallationDetected', 'No installation detected. Set custom path.')
    );
  }

  function initPickers() {
    const pickers = new Map();

    const send = SCPI?.bus?.send || SCPI?.util?.sendToPlugin || globalThis.sendToPlugin;

    function wire(channel, rootId) {
      const picker = SCPI?.ui?.filePicker?.createFilePicker?.({
        rootId,
        displayMode: 'full',
        placeholderText: {key: 'PropertyInspector.ControlPanel.OverridePlaceholder', fallback: 'No override'},
        buttonText: {key: 'PropertyInspector.Common.FilePicker.Button', fallback: 'FILE'},
        selectTitle: {
          key: 'PropertyInspector.ControlPanel.OverrideSelectTitle',
          fallback: 'Select {channel} Data.p4k',
          replacements: {channel: getChannelLabel(channel)}
        },
        clearTitle: {
          key: 'PropertyInspector.ControlPanel.OverrideClearTitle',
          fallback: 'Clear {channel} override',
          replacements: {channel: getChannelLabel(channel)}
        },
        onValueChanged: (value) => {
          send?.('setDataP4KOverride', {
            channel,
            dataP4KPath: value || ''
          });
        }
      });

      pickers.set(channel, picker);
    }

    wire('Live', 'liveP4KPicker');
    wire('Hotfix', 'hotfixP4KPicker');
    wire('Ptu', 'ptuP4KPicker');
    wire('Eptu', 'eptuP4KPicker');
    wire('TechPreview', 'techPreviewP4KPicker');

    return pickers;
  }

  function applyOverridePickerValues(cp, pickers) {
    const list = Array.isArray(cp?.channels) ? cp.channels : [];
    const byChannel = new Map();
    for (const row of list) {
      if (row && typeof row.channel === 'string') {
        byChannel.set(row.channel, row);
      }
    }

    for (const ch of CHANNELS) {
      const picker = pickers.get(ch);
      if (!picker) {
        continue;
      }
      const row = byChannel.get(ch);
      const value = row?.isCustomPath ? String(row?.dataP4KPath || '') : '';
      picker.setValue(value, {persist: false, silent: true});
    }
  }

  SCPI?.util?.onDocumentReady?.(() => {
    SCPI?.bus?.start?.();

    SCPI?.theme?.initThemeDropdown?.({
      rootId: 'themeDropdown',
      linkId: 'pi-theme-styles'
    });

    const pickers = initPickers();
    let pluginLocaleState = createDefaultPluginLocaleState();
    let latestControlPanel = null;

    const send = SCPI?.bus?.send || SCPI?.util?.sendToPlugin || globalThis.sendToPlugin;

    const channelDropdown = SCPI?.ui?.dropdown?.initDropdown?.({
      rootId: 'channelDropdown',
      placeholder: {key: 'PropertyInspector.ControlPanel.ChannelPlaceholder', fallback: 'Preferred channel'},
      searchEnabled: false,
      displaySelectedInInput: true,
      minLoadingMs: 500,
      successFlashMs: 220,
      emptyText: {key: 'PropertyInspector.Common.Dropdown.NoValidChannels', fallback: 'No valid channels'},
      getText: (t) => String(t?.text ?? ''),
      getValue: (t) => String(t?.value ?? ''),
      onSelect: (t) => {
        const value = String(t?.value || '');
        if (!value) {
          return;
        }
        send?.('setChannel', {channel: value});
      }
    });

    const pluginLocaleDropdown = SCPI?.ui?.dropdown?.initDropdown?.({
      rootId: 'pluginLocaleDropdown',
      placeholder: {
        key: 'PropertyInspector.ControlPanel.PluginLocale.Placeholder',
        fallback: 'Plugin language'
      },
      searchEnabled: false,
      displaySelectedInInput: true,
      minLoadingMs: 500,
      successFlashMs: 220,
      emptyText: {
        key: 'PropertyInspector.ControlPanel.PluginLocale.NoLanguagesFound',
        fallback: 'No languages found'
      },
      getText: (t) => String(t?.text ?? ''),
      getValue: (t) => String(t?.value ?? ''),
      onSelect: (t) => {
        const value = normalizePluginLocaleValue(t?.value);
        const isAuto = String(t?.value || '') === AUTO_PLUGIN_LOCALE;
        const nextState = normalizePluginLocaleState({
          mode: isAuto ? AUTO_PLUGIN_LOCALE : OVERRIDE_PLUGIN_LOCALE,
          override: isAuto ? null : value,
          detected: pluginLocaleState.detected,
          effective: isAuto ? pluginLocaleState.detected || pluginLocaleState.effective : value,
          supported: pluginLocaleState.supported
        });

        pluginLocaleState = nextState;
        pluginLocaleDropdown?.setItems?.(buildPluginLocaleItems(pluginLocaleState));
        pluginLocaleDropdown?.setSelectedValue?.(getPluginLocaleSelectionValue(pluginLocaleState), {rerender: true});
        updatePluginLocaleStatus(pluginLocaleState);

        send?.('setPluginLocale', {
          mode: nextState.mode,
          override: nextState.override
        });
      }
    });

    channelDropdown?.setLoading?.(true, {
      key: 'PropertyInspector.Common.Status.LoadingStatus',
      fallback: 'Loading status'
    });
    pluginLocaleDropdown?.setItems?.(buildPluginLocaleItems(pluginLocaleState));
    pluginLocaleDropdown?.setSelectedValue?.(getPluginLocaleSelectionValue(pluginLocaleState), {rerender: true});
    pluginLocaleDropdown?.setLoading?.(true, {
      key: 'PropertyInspector.ControlPanel.PluginLocale.Loading',
      fallback: 'Loading language'
    });
    updatePluginLocaleStatus(pluginLocaleState);

    const factoryResetBtn = document.getElementById('factoryResetBtn');
    const redetectBtn = document.getElementById('forceRedetectBtn');

    factoryResetBtn?.addEventListener('click', () => {
      const ok = globalThis.confirm?.(
        t(
          'PropertyInspector.ControlPanel.FactoryResetConfirm',
          'Factory Reset will remove cached installations, clear all custom Data.p4k overrides, reset theme selection, and rebuild keybindings from scratch.\n\nContinue?'
        )
      );
      if (!ok) {
        return;
      }
      send?.('factoryReset');
    });

    redetectBtn?.addEventListener('click', () => {
      send?.('forceRedetection');
    });

    SCPI?.bus?.on?.((payload) => {
      if (!payload?.controlPanelLoaded) {
        return;
      }

      const cp = payload.controlPanel || {};
      latestControlPanel = cp;
      pluginLocaleState = normalizePluginLocaleState(cp?.pluginLocale);
      pluginLocaleDropdown?.setItems?.(buildPluginLocaleItems(pluginLocaleState));
      pluginLocaleDropdown?.setSelectedValue?.(getPluginLocaleSelectionValue(pluginLocaleState), {rerender: true});
      pluginLocaleDropdown?.setLoading?.(false);
      updatePluginLocaleStatus(pluginLocaleState);

      const items = buildChannelItems(cp);
      channelDropdown?.setItems?.(items);
      channelDropdown?.setLoading?.(false);

      const preferred = String(cp?.preferredChannel || '');
      const current = String(cp?.currentChannel || '');
      const desired = current || preferred;

      const hasDesired = desired && items.some((it) => String(it?.value || '') === desired);
      if (hasDesired) {
        channelDropdown?.setSelectedValue?.(desired, {rerender: true});
      } else if (items.length > 0) {
        channelDropdown?.setSelectedValue?.(String(items[0].value || ''), {rerender: true});
      } else {
        // Clear any stale selection text when no valid channels exist.
        channelDropdown?.setSelectedValue?.('', {rerender: true});
      }

      updateInstallWarning(cp);
      applyOverridePickerValues(cp, pickers);
    });

    SCPI?.i18n?.onChange?.(() => {
      pluginLocaleDropdown?.setItems?.(buildPluginLocaleItems(pluginLocaleState));
      pluginLocaleDropdown?.setSelectedValue?.(getPluginLocaleSelectionValue(pluginLocaleState), {rerender: true});
      updatePluginLocaleStatus(pluginLocaleState);

      if (!latestControlPanel) {
        return;
      }

      const items = buildChannelItems(latestControlPanel);
      channelDropdown?.setItems?.(items);

      const preferred = String(latestControlPanel?.preferredChannel || '');
      const current = String(latestControlPanel?.currentChannel || '');
      const desired = current || preferred;
      const hasDesired = desired && items.some((it) => String(it?.value || '') === desired);

      if (hasDesired) {
        channelDropdown?.setSelectedValue?.(desired, {rerender: true});
      } else if (items.length > 0) {
        channelDropdown?.setSelectedValue?.(String(items[0].value || ''), {rerender: true});
      } else {
        channelDropdown?.setSelectedValue?.('', {rerender: true});
      }

      updateInstallWarning(latestControlPanel);
    });

    // Ask plugin for Control Panel status (themes + channel state).
    SCPI?.bus?.sendOnce?.('pi.connected', 'propertyInspectorConnected');
  });
})();
