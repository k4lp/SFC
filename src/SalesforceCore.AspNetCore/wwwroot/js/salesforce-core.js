/**
 * SalesforceCore JavaScript Library
 * Provides client-side functionality for Salesforce-powered ASP.NET Core applications
 * @version 1.0.0
 */
(function (global) {
    'use strict';

    // Namespace
    const SalesforceCore = global.SalesforceCore = global.SalesforceCore || {};

    // =====================================================
    // LOOKUP COMPONENT
    // =====================================================
    SalesforceCore.Lookup = {
        /** Debounce timers */
        _timers: {},

        /** Cache for recent searches */
        _cache: {},

        /** Currently active dropdown */
        _activeDropdown: null,

        /**
         * Initialize all lookup components on the page
         */
        init: function () {
            const lookups = document.querySelectorAll('[data-sf-lookup="true"]');
            lookups.forEach(input => this._initLookup(input));

            // Global click handler to close dropdowns
            document.addEventListener('click', (e) => {
                if (!e.target.closest('.sf-lookup-container')) {
                    this._closeAllDropdowns();
                }
            });

            // Clear button handlers
            document.querySelectorAll('[data-sf-clear]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.preventDefault();
                    const targetId = btn.getAttribute('data-sf-clear');
                    this._clearLookup(targetId);
                });
            });
        },

        /**
         * Initialize a single lookup component
         */
        _initLookup: function (input) {
            const targetId = input.getAttribute('data-sf-target');
            const minChars = parseInt(input.getAttribute('data-sf-min-chars')) || 2;
            const debounceMs = parseInt(input.getAttribute('data-sf-debounce')) || 300;

            // Input event for search
            input.addEventListener('input', () => {
                const query = input.value.trim();

                // Clear timer
                if (this._timers[targetId]) {
                    clearTimeout(this._timers[targetId]);
                }

                // Clear hidden value when typing
                const hiddenInput = document.getElementById(targetId);
                if (hiddenInput) {
                    hiddenInput.value = '';
                }

                // Hide clear button
                const clearBtn = document.querySelector(`[data-sf-clear="${targetId}"]`);
                if (clearBtn) {
                    clearBtn.style.display = 'none';
                }

                // Check minimum characters
                if (query.length < minChars) {
                    this._hideDropdown(targetId);
                    return;
                }

                // Debounced search
                this._timers[targetId] = setTimeout(() => {
                    this._search(input, query);
                }, debounceMs);
            });

            // Keyboard navigation
            input.addEventListener('keydown', (e) => {
                this._handleKeydown(e, targetId);
            });

            // Focus handler
            input.addEventListener('focus', () => {
                const query = input.value.trim();
                if (query.length >= minChars) {
                    const dropdown = document.getElementById(targetId + '_dropdown');
                    if (dropdown && dropdown.querySelector('.sf-lookup-results').innerHTML) {
                        dropdown.style.display = 'block';
                    }
                }
            });
        },

        /**
         * Perform search
         */
        _search: async function (input, query) {
            const targetId = input.getAttribute('data-sf-target');
            const targetObjects = input.getAttribute('data-sf-target-objects');
            const searchUrl = input.getAttribute('data-sf-search-url') || '/Lookup/Search';
            const limit = parseInt(input.getAttribute('data-sf-limit')) || 10;
            const searchFields = input.getAttribute('data-sf-search-fields');
            const displayTemplate = input.getAttribute('data-sf-display-template');

            const dropdown = document.getElementById(targetId + '_dropdown');
            if (!dropdown) return;

            const resultsContainer = dropdown.querySelector('.sf-lookup-results');
            const loadingEl = dropdown.querySelector('.sf-lookup-loading');
            const emptyEl = dropdown.querySelector('.sf-lookup-empty');

            // Show dropdown and loading
            dropdown.style.display = 'block';
            loadingEl.style.display = 'block';
            resultsContainer.innerHTML = '';
            emptyEl.style.display = 'none';
            this._activeDropdown = dropdown;

            try {
                // Check cache
                const cacheKey = `${targetObjects}:${query}`;
                let results = this._cache[cacheKey];

                if (!results) {
                    // Build search URL
                    const params = new URLSearchParams({
                        q: query,
                        targetObjects: targetObjects,
                        limit: limit
                    });

                    if (searchFields) {
                        params.append('searchFields', searchFields);
                    }

                    const response = await fetch(`${searchUrl}?${params.toString()}`, {
                        headers: {
                            'Accept': 'application/json',
                            'X-Requested-With': 'XMLHttpRequest'
                        }
                    });

                    if (!response.ok) {
                        throw new Error('Search request failed');
                    }

                    results = await response.json();
                    this._cache[cacheKey] = results;

                    // Clear cache after 5 minutes
                    setTimeout(() => {
                        delete this._cache[cacheKey];
                    }, 5 * 60 * 1000);
                }

                loadingEl.style.display = 'none';

                // Render results
                if (!results.items || results.items.length === 0) {
                    emptyEl.style.display = 'block';
                } else {
                    this._renderResults(resultsContainer, results.items, targetId, displayTemplate);
                }
            } catch (error) {
                console.error('Lookup search error:', error);
                loadingEl.style.display = 'none';
                emptyEl.textContent = 'Error searching records';
                emptyEl.style.display = 'block';
            }
        },

        /**
         * Render search results
         */
        _renderResults: function (container, items, targetId, displayTemplate) {
            items.forEach((item, index) => {
                const div = document.createElement('div');
                div.className = 'sf-lookup-item';
                div.setAttribute('data-sf-value', item.id);
                div.setAttribute('data-sf-display', item.displayName || item.name);
                div.setAttribute('data-sf-object-type', item.objectType || '');
                div.setAttribute('tabindex', '0');

                // Format display text
                let displayText = item.displayName || item.name || item.id;
                if (displayTemplate) {
                    displayText = this._formatTemplate(displayTemplate, item);
                }

                // Build HTML
                let html = `<span class="sf-lookup-item-name">${this._escapeHtml(displayText)}</span>`;
                if (item.objectType) {
                    html += `<span class="sf-lookup-item-type">${this._escapeHtml(item.objectType)}</span>`;
                }
                if (item.subtitle) {
                    html += `<span class="sf-lookup-item-subtitle">${this._escapeHtml(item.subtitle)}</span>`;
                }

                div.innerHTML = html;

                // Click handler
                div.addEventListener('click', () => {
                    this._selectItem(targetId, item.id, displayText);
                });

                // Keyboard handler
                div.addEventListener('keydown', (e) => {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        this._selectItem(targetId, item.id, displayText);
                    }
                });

                container.appendChild(div);
            });
        },

        /**
         * Select an item from results
         */
        _selectItem: function (targetId, value, displayText) {
            // Set hidden input value
            const hiddenInput = document.getElementById(targetId);
            if (hiddenInput) {
                hiddenInput.value = value;
                // Trigger change event
                hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
            }

            // Set display input value
            const displayInput = document.getElementById(targetId + '_display');
            if (displayInput) {
                displayInput.value = displayText;
            }

            // Show clear button
            const clearBtn = document.querySelector(`[data-sf-clear="${targetId}"]`);
            if (clearBtn) {
                clearBtn.style.display = 'flex';
            }

            // Hide dropdown
            this._hideDropdown(targetId);

            // Fire custom event
            document.dispatchEvent(new CustomEvent('sf:lookup:select', {
                detail: { targetId, value, displayText }
            }));
        },

        /**
         * Clear lookup selection
         */
        _clearLookup: function (targetId) {
            // Clear hidden input
            const hiddenInput = document.getElementById(targetId);
            if (hiddenInput) {
                hiddenInput.value = '';
                hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
            }

            // Clear display input
            const displayInput = document.getElementById(targetId + '_display');
            if (displayInput) {
                displayInput.value = '';
                displayInput.focus();
            }

            // Hide clear button
            const clearBtn = document.querySelector(`[data-sf-clear="${targetId}"]`);
            if (clearBtn) {
                clearBtn.style.display = 'none';
            }

            // Fire custom event
            document.dispatchEvent(new CustomEvent('sf:lookup:clear', {
                detail: { targetId }
            }));
        },

        /**
         * Handle keyboard navigation
         */
        _handleKeydown: function (e, targetId) {
            const dropdown = document.getElementById(targetId + '_dropdown');
            if (!dropdown || dropdown.style.display === 'none') {
                return;
            }

            const items = dropdown.querySelectorAll('.sf-lookup-item');
            const currentIndex = Array.from(items).findIndex(item =>
                item.classList.contains('sf-lookup-item-active')
            );

            switch (e.key) {
                case 'ArrowDown':
                    e.preventDefault();
                    if (currentIndex < items.length - 1) {
                        items[currentIndex]?.classList.remove('sf-lookup-item-active');
                        items[currentIndex + 1]?.classList.add('sf-lookup-item-active');
                        items[currentIndex + 1]?.scrollIntoView({ block: 'nearest' });
                    } else if (currentIndex === -1 && items.length > 0) {
                        items[0].classList.add('sf-lookup-item-active');
                    }
                    break;

                case 'ArrowUp':
                    e.preventDefault();
                    if (currentIndex > 0) {
                        items[currentIndex]?.classList.remove('sf-lookup-item-active');
                        items[currentIndex - 1]?.classList.add('sf-lookup-item-active');
                        items[currentIndex - 1]?.scrollIntoView({ block: 'nearest' });
                    }
                    break;

                case 'Enter':
                    e.preventDefault();
                    const activeItem = dropdown.querySelector('.sf-lookup-item-active');
                    if (activeItem) {
                        activeItem.click();
                    }
                    break;

                case 'Escape':
                    e.preventDefault();
                    this._hideDropdown(targetId);
                    break;
            }
        },

        /**
         * Hide dropdown
         */
        _hideDropdown: function (targetId) {
            const dropdown = document.getElementById(targetId + '_dropdown');
            if (dropdown) {
                dropdown.style.display = 'none';
            }
            if (this._activeDropdown === dropdown) {
                this._activeDropdown = null;
            }
        },

        /**
         * Close all dropdowns
         */
        _closeAllDropdowns: function () {
            document.querySelectorAll('.sf-lookup-dropdown').forEach(dropdown => {
                dropdown.style.display = 'none';
            });
            this._activeDropdown = null;
        },

        /**
         * Format display template
         */
        _formatTemplate: function (template, item) {
            return template.replace(/\{(\w+)\}/g, (match, key) => {
                return item[key] || item[key.toLowerCase()] || '';
            });
        },

        /**
         * Escape HTML
         */
        _escapeHtml: function (text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
    };

    // =====================================================
    // DEPENDENT PICKLIST COMPONENT
    // =====================================================
    SalesforceCore.DependentPicklist = {
        /** Store dependency maps */
        _dependencyMaps: {},

        /**
         * Initialize all dependent picklists
         */
        init: function () {
            const dependentSelects = document.querySelectorAll('[data-sf-dependent="true"]');
            dependentSelects.forEach(select => this._initDependentPicklist(select));
        },

        /**
         * Initialize a single dependent picklist
         */
        _initDependentPicklist: function (select) {
            const controllerName = select.getAttribute('data-sf-controller');
            const dependencyMapJson = select.getAttribute('data-sf-dependency-map');

            if (!controllerName) return;

            // Parse dependency map
            let dependencyMap = {};
            if (dependencyMapJson) {
                try {
                    dependencyMap = JSON.parse(dependencyMapJson);
                    this._dependencyMaps[select.id] = dependencyMap;
                } catch (e) {
                    console.error('Failed to parse dependency map:', e);
                }
            }

            // Store all original options
            const allOptions = Array.from(select.options).map(opt => ({
                value: opt.value,
                text: opt.text,
                selected: opt.selected
            }));
            select._sfAllOptions = allOptions;

            // Find controller element
            const controller = document.querySelector(`[name="${controllerName}"], #${controllerName}`);
            if (!controller) {
                console.warn(`Controller "${controllerName}" not found for dependent picklist`);
                return;
            }

            // Initial update
            this._updateOptions(select, controller.value);

            // Listen for controller changes
            controller.addEventListener('change', () => {
                this._updateOptions(select, controller.value);
            });
        },

        /**
         * Update dependent picklist options based on controlling value
         */
        _updateOptions: function (select, controllingValue) {
            const dependencyMap = this._dependencyMaps[select.id] || {};
            const allOptions = select._sfAllOptions || [];

            // Get valid values for controlling value
            const validValues = dependencyMap[controllingValue] || [];
            const validSet = new Set(validValues);

            // Clear current options
            select.innerHTML = '';

            // Add back valid options
            allOptions.forEach(opt => {
                // Always include blank option
                if (!opt.value) {
                    const option = document.createElement('option');
                    option.value = '';
                    option.text = opt.text;
                    select.appendChild(option);
                    return;
                }

                // Check if value is valid for current controlling value
                if (validSet.size === 0 || validSet.has(opt.value)) {
                    const option = document.createElement('option');
                    option.value = opt.value;
                    option.text = opt.text;
                    if (opt.selected && validSet.has(opt.value)) {
                        option.selected = true;
                    }
                    select.appendChild(option);
                }
            });

            // If no option is selected and we have options, select first
            if (select.value === '' && select.options.length > 0) {
                // Keep blank selected if available
            }

            // Trigger change event
            select.dispatchEvent(new Event('change', { bubbles: true }));

            // Fire custom event
            document.dispatchEvent(new CustomEvent('sf:picklist:updated', {
                detail: { selectId: select.id, controllingValue }
            }));
        },

        /**
         * Manually update a dependent picklist
         */
        update: function (selectId, controllingValue) {
            const select = document.getElementById(selectId);
            if (select) {
                this._updateOptions(select, controllingValue);
            }
        }
    };

    // =====================================================
    // FORM UTILITIES
    // =====================================================
    SalesforceCore.Form = {
        /**
         * Initialize form utilities
         */
        init: function () {
            // Anti-forgery token injection
            this._injectAntiForgeryTokens();

            // Form submission handling
            document.querySelectorAll('form.sf-model-form').forEach(form => {
                this._initForm(form);
            });
        },

        /**
         * Inject anti-forgery tokens into forms
         */
        _injectAntiForgeryTokens: function () {
            const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
            if (!tokenInput || !tokenInput.value) {
                // Try to get from cookie
                const token = this._getAntiForgeryTokenFromCookie();
                if (token) {
                    document.querySelectorAll('input[data-sf-antiforgery="true"]').forEach(input => {
                        input.value = token;
                    });
                }
            }
        },

        /**
         * Get anti-forgery token from cookie
         */
        _getAntiForgeryTokenFromCookie: function () {
            const name = '.AspNetCore.Antiforgery.';
            const cookies = document.cookie.split(';');
            for (let cookie of cookies) {
                cookie = cookie.trim();
                if (cookie.indexOf(name) === 0 || cookie.indexOf('XSRF-TOKEN') === 0) {
                    return cookie.substring(cookie.indexOf('=') + 1);
                }
            }
            return null;
        },

        /**
         * Initialize a form
         */
        _initForm: function (form) {
            form.addEventListener('submit', (e) => {
                // Validate before submit
                if (!form.checkValidity()) {
                    e.preventDefault();
                    form.reportValidity();
                    return;
                }

                // Show loading state
                const submitBtn = form.querySelector('button[type="submit"]');
                if (submitBtn) {
                    submitBtn.disabled = true;
                    submitBtn.dataset.originalText = submitBtn.textContent;
                    submitBtn.textContent = 'Saving...';
                }
            });
        },

        /**
         * Reset form loading state
         */
        resetLoadingState: function (form) {
            const submitBtn = form.querySelector('button[type="submit"]');
            if (submitBtn && submitBtn.dataset.originalText) {
                submitBtn.disabled = false;
                submitBtn.textContent = submitBtn.dataset.originalText;
            }
        }
    };

    // =====================================================
    // TOAST NOTIFICATIONS
    // =====================================================
    SalesforceCore.Toast = {
        /** Container element */
        _container: null,

        /** Default options */
        _defaults: {
            position: 'top-right',
            duration: 5000,
            closable: true
        },

        /**
         * Initialize toast container
         */
        init: function (options = {}) {
            this._defaults = { ...this._defaults, ...options };
            this._ensureContainer();
        },

        /**
         * Ensure toast container exists
         */
        _ensureContainer: function () {
            if (this._container) return;

            this._container = document.createElement('div');
            this._container.className = 'sf-toast-container sf-toast-' + this._defaults.position;
            document.body.appendChild(this._container);
        },

        /**
         * Show a toast message
         */
        show: function (message, type = 'info', options = {}) {
            this._ensureContainer();

            const opts = { ...this._defaults, ...options };

            const toast = document.createElement('div');
            toast.className = `sf-toast sf-toast-${type}`;

            // Icon
            const icon = this._getIcon(type);
            if (icon) {
                const iconEl = document.createElement('span');
                iconEl.className = 'sf-toast-icon';
                iconEl.innerHTML = icon;
                toast.appendChild(iconEl);
            }

            // Message
            const msgEl = document.createElement('span');
            msgEl.className = 'sf-toast-message';
            msgEl.textContent = message;
            toast.appendChild(msgEl);

            // Close button
            if (opts.closable) {
                const closeBtn = document.createElement('button');
                closeBtn.className = 'sf-toast-close';
                closeBtn.innerHTML = '&times;';
                closeBtn.addEventListener('click', () => this._dismiss(toast));
                toast.appendChild(closeBtn);
            }

            this._container.appendChild(toast);

            // Animate in
            requestAnimationFrame(() => {
                toast.classList.add('sf-toast-visible');
            });

            // Auto dismiss
            if (opts.duration > 0) {
                setTimeout(() => this._dismiss(toast), opts.duration);
            }

            return toast;
        },

        /**
         * Dismiss a toast
         */
        _dismiss: function (toast) {
            toast.classList.remove('sf-toast-visible');
            toast.classList.add('sf-toast-hiding');
            setTimeout(() => {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 300);
        },

        /**
         * Get icon for toast type
         */
        _getIcon: function (type) {
            const icons = {
                success: '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>',
                error: '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>',
                warning: '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z"/></svg>',
                info: '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>'
            };
            return icons[type] || icons.info;
        },

        // Convenience methods
        success: function (message, options) { return this.show(message, 'success', options); },
        error: function (message, options) { return this.show(message, 'error', options); },
        warning: function (message, options) { return this.show(message, 'warning', options); },
        info: function (message, options) { return this.show(message, 'info', options); }
    };

    // =====================================================
    // MUTATION OBSERVER
    // =====================================================
    SalesforceCore.Observer = {
        _observer: null,

        /**
         * Start observing for dynamically added elements
         */
        start: function () {
            if (this._observer) return;

            this._observer = new MutationObserver((mutations) => {
                let needsInit = false;

                mutations.forEach((mutation) => {
                    mutation.addedNodes.forEach((node) => {
                        if (node.nodeType === Node.ELEMENT_NODE) {
                            // Check if new node contains SalesforceCore components
                            if (node.querySelector('[data-sf-lookup], [data-sf-dependent]')) {
                                needsInit = true;
                            }
                            if (node.matches('[data-sf-lookup], [data-sf-dependent]')) {
                                needsInit = true;
                            }
                        }
                    });
                });

                if (needsInit) {
                    SalesforceCore.init();
                }
            });

            this._observer.observe(document.body, {
                childList: true,
                subtree: true
            });
        },

        /**
         * Stop observing
         */
        stop: function () {
            if (this._observer) {
                this._observer.disconnect();
                this._observer = null;
            }
        }
    };

    // =====================================================
    // MAIN INITIALIZATION
    // =====================================================

    /**
     * Initialize all SalesforceCore components
     */
    SalesforceCore.init = function () {
        SalesforceCore.Lookup.init();
        SalesforceCore.DependentPicklist.init();
        SalesforceCore.Form.init();
        SalesforceCore.Toast.init();
    };

    /**
     * Start mutation observer for dynamic content
     */
    SalesforceCore.enableDynamicSupport = function () {
        SalesforceCore.Observer.start();
    };

    // Auto-initialize on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            SalesforceCore.init();
        });
    } else {
        // DOM already loaded
        SalesforceCore.init();
    }

})(typeof window !== 'undefined' ? window : this);
