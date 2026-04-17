(function () {
  const SCPI = globalThis.SCPI;
  SCPI?.i18n?.init?.();
  SCPI?.i18n?.apply?.();
  SCPI?.bus?.start?.();
  SCPI?.theme?.initThemeDropdown?.();

  const slotConfigs = [
    {
      rootId: 'rotateLeftDropdown',
      settingsKey: 'rotateLeftFunction',
      slotLabelKey: 'PropertyInspector.DialAction.RotateLeftLabel',
      slotLabelFallback: 'Rotate Left',
      placeholderKey: 'PropertyInspector.DialAction.RotateLeftPlaceholder',
      placeholderFallback: 'Select rotate-left function...',
      idPrefix: 'rotateLeft'
    },
    {
      rootId: 'rotateRightDropdown',
      settingsKey: 'rotateRightFunction',
      slotLabelKey: 'PropertyInspector.DialAction.RotateRightLabel',
      slotLabelFallback: 'Rotate Right',
      placeholderKey: 'PropertyInspector.DialAction.RotateRightPlaceholder',
      placeholderFallback: 'Select rotate-right function...',
      idPrefix: 'rotateRight'
    },
    {
      rootId: 'pressDropdown',
      settingsKey: 'pressFunction',
      slotLabelKey: 'PropertyInspector.DialAction.PushLabel',
      slotLabelFallback: 'Push',
      placeholderKey: 'PropertyInspector.DialAction.PushPlaceholder',
      placeholderFallback: 'Select push function...',
      idPrefix: 'press'
    }
  ];

  let allOptions = [];
  let isSelectingOption = false;

  const slotStates = slotConfigs.map((config) => {
    const dropdown = SCPI?.ui?.dropdown?.initDropdown?.({
      rootId: config.rootId,
      placeholder: {key: config.placeholderKey, fallback: config.placeholderFallback},
      searchEnabled: true,
      minLoadingMs: 0,
      successFlashMs: 100,
      emptyText: {
        key: 'PropertyInspector.Common.Dropdown.NoMatchingFunctionsFound',
        fallback: 'No matching functions found'
      },
      maxResults: 50,
      getText: (opt) => String(opt?.text ?? ''),
      getValue: (opt) => String(opt?.value ?? ''),
      getGroup: (opt) => String(opt?.group ?? ''),
      isDisabled: (opt) => !!opt?.disabled,
      onSelect: (opt) => selectOption(config.settingsKey, opt, {persist: true})
    });

    dropdown?.setLoading?.(true, {
      key: 'PropertyInspector.Common.Status.LoadingFunctions',
      fallback: 'Loading functions'
    });

    const [getValue, setValue] = globalThis.SDPIComponents.useSettings(
      config.settingsKey,
      (value) => {
        if (!isSelectingOption) {
          syncSelection(config.settingsKey, value);
        }
      }
    );

    return {
      ...config,
      dropdown,
      getValue,
      setValue,
      currentValue: ''
    };
  });

  SCPI?.ui?.filePicker?.createFilePicker?.({
    rootId: 'audioFilePicker',
    placeholderText: {key: 'PropertyInspector.Common.FilePicker.NoFileSelected', fallback: 'No file selected'},
    buttonText: {key: 'PropertyInspector.Common.FilePicker.Button', fallback: 'FILE'},
    selectTitle: {key: 'PropertyInspector.Common.Audio.SelectTitle', fallback: 'Select audio file'},
    clearTitle: {key: 'PropertyInspector.Common.Audio.ClearTitle', fallback: 'Clear audio file'},
    settingsKey: 'clickSoundPath'
  });

  function flattenFunctionsData(groups) {
    const flatten = SCPI?.functionPicker?.flattenFunctionsData;
    if (typeof flatten !== 'function') {
      return [];
    }

    return flatten(groups, {
      requireToggleCandidates: false,
      excludeBindingTypes: ['mouseaxis', 'joystick', 'gamepad']
    });
  }

  function populateFunctionsDropdowns(functionsData) {
    allOptions = flattenFunctionsData(functionsData);

    slotStates.forEach((slot) => {
      slot.dropdown?.setItems?.(allOptions);
      slot.dropdown?.setSelectedValue?.(slot.currentValue, {rerender: false});

      if (slot.currentValue) {
        syncSelection(slot.settingsKey, slot.currentValue);
      }
    });
  }

  function syncSelection(settingsKey, value) {
    const slot = slotStates.find((entry) => entry.settingsKey === settingsKey);
    if (!slot) {
      return;
    }

    slot.currentValue = value || '';

    const opt = allOptions.find((entry) => entry.value === value || entry.legacyValue === value);
    if (!opt) {
      if (!value) {
        updateFunctionDetails(slot, null);
      }
      return;
    }

    const isLegacyMatch = opt.legacyValue === value && opt.value !== value;
    selectOption(settingsKey, opt, {persist: isLegacyMatch});
  }

  function selectOption(settingsKey, opt, opts = {}) {
    const slot = slotStates.find((entry) => entry.settingsKey === settingsKey);
    if (!slot) {
      return;
    }

    const persist = opts.persist !== false;

    isSelectingOption = true;
    slot.currentValue = opt.value;
    slot.dropdown?.setSelectedValue?.(opt.value, {rerender: true});
    updateFunctionDetails(slot, opt);

    if (persist) {
      slot.setValue(opt.value);
    }

    setTimeout(() => {
      isSelectingOption = false;
    }, 200);
  }

  function updateFunctionDetails(slot, opt) {
    if (!slot || !slot.idPrefix) {
      return;
    }

    const updateDetails = SCPI?.functionPicker?.updateFunctionDetails;
    if (typeof updateDetails !== 'function') {
      return;
    }

    const prefix = slot.idPrefix;
    const slotLabel = SCPI?.i18n?.t(slot.slotLabelKey, slot.slotLabelFallback) || slot.slotLabelFallback;
    updateDetails({
      containerEl: document.getElementById(`${prefix}-details-container`),
      titleEl: document.getElementById(`${prefix}-details-title`),
      descriptionEl: document.getElementById(`${prefix}-description`),
      bindingElements: {
        keyboard: document.getElementById(`${prefix}-binding-keyboard`),
        mouse: document.getElementById(`${prefix}-binding-mouse`),
        gamepad: document.getElementById(`${prefix}-binding-gamepad`),
        joystick: document.getElementById(`${prefix}-binding-joystick`)
      },
      hideContainerWhenEmpty: true,
      showContainerWhenFilled: true,
      slotLabel,
      selectedOption: opt
    });
  }

  SCPI?.bus?.on?.((payload) => {
    const loaded = payload?.functionsLoaded;

    if (loaded === true) {
      slotStates.forEach((slot) => {
        slot.dropdown?.setLoading?.(false);
        document.getElementById(slot.rootId)?.classList.remove('pi-dropdown--error');
      });

      populateFunctionsDropdowns(payload.functions || []);
    }

    if (loaded === false) {
      slotStates.forEach((slot) => {
        document.getElementById(slot.rootId)?.classList.add('pi-dropdown--error');
        slot.dropdown?.setLoading?.(true, {
          key: 'PropertyInspector.Common.Status.NoInstallationDetected',
          fallback: 'No installation detected. Set custom path.'
        });
        slot.dropdown?.setItems?.([]);
        updateFunctionDetails(slot, null);
      });
    }
  });

  SCPI?.i18n?.onChange?.(() => {
    slotStates.forEach((slot) => {
      const opt = allOptions.find((entry) => entry.value === slot.currentValue || entry.legacyValue === slot.currentValue) || null;
      updateFunctionDetails(slot, opt);
    });
  });

  SCPI?.util?.onDocumentReady?.(() => {
    slotStates.forEach((slot) => {
      const savedValue = slot.getValue();
      slot.currentValue = savedValue || '';
      slot.dropdown?.setSelectedValue?.(slot.currentValue, {rerender: false});
      updateFunctionDetails(slot, null);
    });

    SCPI?.bus?.sendOnce?.('pi.connected', 'propertyInspectorConnected');
  });
})();
