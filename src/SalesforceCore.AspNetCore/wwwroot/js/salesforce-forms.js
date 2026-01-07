/**
 * SalesforceCore Forms Handler
 * Provides form validation, field conversion, and dynamic behavior
 *
 * Selectors can be customized via the configure() function:
 * - formSelector: Selector for forms (default: '#sf-record-form')
 * - addressContainerSelector: Selector for address containers (default: '.sf-address-container')
 * - addressCopySelector: Selector for address copy button (default: '.sf-address-copy')
 * - addressClearSelector: Selector for address clear button (default: '.sf-address-clear')
 * - addressMapSelector: Selector for address map button (default: '.sf-address-map')
 */
const SalesforceForms = (function() {
    'use strict';

    // Default configuration - can be overridden via configure()
    const config = {
        selectors: {
            form: '#sf-record-form',
            addressContainer: '.sf-address-container',
            addressCopy: '.sf-address-copy',
            addressClear: '.sf-address-clear',
            addressMap: '.sf-address-map',
            // Address field name suffixes
            addressStreet: '[name$="Street"]',
            addressCity: '[name$="City"]',
            addressState: '[name$="State"]',
            addressPostalCode: '[name$="PostalCode"]',
            addressCountry: '[name$="Country"]'
        }
    };

    /**
     * Initialize form handling
     */
    function init() {
        initializeFormValidation();
        initializeFieldBehaviors();
        initializeMultiPicklists();
        initializeAddressFields();
    }

    /**
     * Initialize form validation
     */
    function initializeFormValidation() {
        document.querySelectorAll(config.selectors.form).forEach(form => {
            form.addEventListener('submit', function(e) {
                if (!form.checkValidity()) {
                    e.preventDefault();
                    e.stopPropagation();

                    // Focus first invalid field
                    const firstInvalid = form.querySelector(':invalid');
                    if (firstInvalid) {
                        firstInvalid.focus();
                        firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    }
                }
                form.classList.add('was-validated');
            });
        });
    }

    /**
     * Initialize field-specific behaviors
     */
    function initializeFieldBehaviors() {
        // Currency fields - format on blur
        document.querySelectorAll('input[type="number"][name$="Amount"], input[type="number"][name$="Price"], input[type="number"][name$="Cost"]').forEach(input => {
            input.addEventListener('blur', function() {
                if (this.value) {
                    this.value = parseFloat(this.value).toFixed(2);
                }
            });
        });

        // Phone fields - format
        document.querySelectorAll('input[type="tel"]').forEach(input => {
            input.addEventListener('blur', function() {
                // Basic phone formatting
                const value = this.value.replace(/\D/g, '');
                if (value.length === 10) {
                    this.value = `(${value.slice(0,3)}) ${value.slice(3,6)}-${value.slice(6)}`;
                }
            });
        });

        // Conditional field visibility
        document.querySelectorAll('[data-show-when]').forEach(field => {
            const conditions = JSON.parse(field.dataset.showWhen);
            setupConditionalVisibility(field, conditions);
        });
    }

    /**
     * Initialize multi-picklist handling
     */
    function initializeMultiPicklists() {
        document.querySelectorAll('select[multiple]').forEach(select => {
            // Store selected values as semicolon-separated
            select.addEventListener('change', function() {
                const values = Array.from(this.selectedOptions).map(o => o.value);

                // Create or update hidden input
                let hidden = this.parentElement.querySelector(`input[name="${this.name}_values"]`);
                if (!hidden) {
                    hidden = document.createElement('input');
                    hidden.type = 'hidden';
                    hidden.name = this.name;
                    this.parentElement.appendChild(hidden);
                }
                hidden.value = values.join(';');
            });

            // Disable the select's name to prevent duplicate submission
            select.name = select.name + '_display';
        });
    }

    /**
     * Initialize address field compound handling
     */
    function initializeAddressFields() {
        document.querySelectorAll(config.selectors.addressContainer).forEach(container => {
            const copyBtn = container.querySelector(config.selectors.addressCopy);
            const clearBtn = container.querySelector(config.selectors.addressClear);
            const mapBtn = container.querySelector(config.selectors.addressMap);

            if (copyBtn) {
                copyBtn.addEventListener('click', function() {
                    copyAddressToClipboard(container);
                });
            }

            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    clearAddressFields(container);
                });
            }

            if (mapBtn) {
                mapBtn.addEventListener('click', function() {
                    openInMaps(container);
                });
            }
        });
    }

    /**
     * Setup conditional field visibility
     */
    function setupConditionalVisibility(field, conditions) {
        const checkVisibility = () => {
            let visible = true;
            for (const [fieldName, expectedValue] of Object.entries(conditions)) {
                const controlField = document.querySelector(`[name="${fieldName}"]`);
                if (controlField) {
                    const currentValue = controlField.type === 'checkbox'
                        ? controlField.checked.toString()
                        : controlField.value;
                    if (currentValue !== expectedValue) {
                        visible = false;
                        break;
                    }
                }
            }

            field.style.display = visible ? '' : 'none';

            // Disable hidden required fields
            const inputs = field.querySelectorAll('input, select, textarea');
            inputs.forEach(input => {
                if (!visible && input.required) {
                    input.dataset.wasRequired = 'true';
                    input.required = false;
                } else if (visible && input.dataset.wasRequired === 'true') {
                    input.required = true;
                }
            });
        };

        // Watch control fields
        for (const fieldName of Object.keys(conditions)) {
            const controlField = document.querySelector(`[name="${fieldName}"]`);
            if (controlField) {
                controlField.addEventListener('change', checkVisibility);
            }
        }

        // Initial check
        checkVisibility();
    }

    /**
     * Copy address to clipboard
     */
    function copyAddressToClipboard(container) {
        const street = container.querySelector(config.selectors.addressStreet)?.value || '';
        const city = container.querySelector(config.selectors.addressCity)?.value || '';
        const state = container.querySelector(config.selectors.addressState)?.value || '';
        const postal = container.querySelector(config.selectors.addressPostalCode)?.value || '';
        const country = container.querySelector(config.selectors.addressCountry)?.value || '';

        const parts = [street, city, state, postal, country].filter(p => p);
        const address = parts.join(', ');

        if (address) {
            navigator.clipboard.writeText(address).then(() => {
                showToast('Address copied to clipboard');
            });
        }
    }

    /**
     * Clear all address fields
     */
    function clearAddressFields(container) {
        container.querySelectorAll('input').forEach(input => {
            input.value = '';
        });
    }

    /**
     * Open address in maps
     */
    function openInMaps(container) {
        const street = container.querySelector(config.selectors.addressStreet)?.value || '';
        const city = container.querySelector(config.selectors.addressCity)?.value || '';
        const state = container.querySelector(config.selectors.addressState)?.value || '';
        const postal = container.querySelector(config.selectors.addressPostalCode)?.value || '';
        const country = container.querySelector(config.selectors.addressCountry)?.value || '';

        const parts = [street, city, state, postal, country].filter(p => p);
        const address = parts.join(', ');

        if (address) {
            const url = `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
            window.open(url, '_blank');
        }
    }

    /**
     * Show toast notification
     */
    function showToast(message, type = 'success') {
        // Check for Bootstrap toast or create simple one
        const toastContainer = document.getElementById('toast-container') || createToastContainer();

        const toast = document.createElement('div');
        toast.className = `toast align-items-center text-white bg-${type} border-0`;
        toast.setAttribute('role', 'alert');
        toast.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">${message}</div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
            </div>
        `;

        toastContainer.appendChild(toast);

        if (typeof bootstrap !== 'undefined') {
            const bsToast = new bootstrap.Toast(toast, { delay: 3000 });
            bsToast.show();
        } else {
            toast.style.display = 'block';
            setTimeout(() => toast.remove(), 3000);
        }
    }

    /**
     * Create toast container
     */
    function createToastContainer() {
        const container = document.createElement('div');
        container.id = 'toast-container';
        container.className = 'toast-container position-fixed bottom-0 end-0 p-3';
        document.body.appendChild(container);
        return container;
    }

    /**
     * Build form data for submission
     */
    function buildFormData(form) {
        const formData = new FormData(form);
        const data = {};

        formData.forEach((value, key) => {
            // Skip system fields
            if (key.startsWith('__') || key === 'RequestVerificationToken') {
                return;
            }

            // Handle multi-value fields
            if (data[key]) {
                if (Array.isArray(data[key])) {
                    data[key].push(value);
                } else {
                    data[key] = [data[key], value];
                }
            } else {
                data[key] = value;
            }
        });

        return data;
    }

    /**
     * Configure the forms module
     * @param {Object} options - Configuration options
     */
    function configure(options) {
        if (options.selectors) {
            Object.assign(config.selectors, options.selectors);
        }
    }

    // Public API
    return {
        init: init,
        showToast: showToast,
        buildFormData: buildFormData,
        configure: configure,
        config: config
    };
})();

// Auto-initialize on DOM ready
document.addEventListener('DOMContentLoaded', SalesforceForms.init);

// Re-initialize after HTMX swaps
document.body.addEventListener('htmx:afterSettle', function() {
    SalesforceForms.init();
});

// Handle HTMX errors
document.body.addEventListener('htmx:responseError', function(evt) {
    const message = evt.detail.xhr.responseText || 'An error occurred';
    SalesforceForms.showToast(message, 'danger');
});
