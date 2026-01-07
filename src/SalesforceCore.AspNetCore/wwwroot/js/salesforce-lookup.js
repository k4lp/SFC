/**
 * SalesforceCore Lookup Field Handler
 * Provides intelligent search, selection, and dependent lookup functionality
 *
 * Selectors can be customized via data attributes on the container:
 * - data-search-selector: Selector for search input (default: '.sf-lookup-search')
 * - data-results-selector: Selector for results div (default: '.sf-lookup-results')
 * - data-clear-selector: Selector for clear button (default: '.sf-lookup-clear')
 * - data-id-selector: Selector for hidden ID input (default: '.sf-lookup-id')
 * - data-view-selector: Selector for view button (default: '.sf-lookup-view')
 * - data-item-selector: Selector for result items (default: '.sf-lookup-item')
 */
const SalesforceLookup = (function() {
    'use strict';

    // Default configuration - can be overridden via container data attributes
    const defaultConfig = {
        minSearchLength: 2,
        searchDelay: 300,
        lookupSearchUrl: '/Lookup/Search',
        // Default selectors
        selectors: {
            container: '.sf-lookup-container',
            search: '.sf-lookup-search',
            results: '.sf-lookup-results',
            clear: '.sf-lookup-clear',
            id: '.sf-lookup-id',
            view: '.sf-lookup-view',
            item: '.sf-lookup-item',
            itemActive: '.sf-lookup-item.active'
        }
    };

    // Current config (can be modified)
    const config = { ...defaultConfig };

    let searchTimeout = null;

    /**
     * Get selectors for a specific container (merges defaults with data attributes)
     */
    function getSelectors(container) {
        return {
            search: container.dataset.searchSelector || config.selectors.search,
            results: container.dataset.resultsSelector || config.selectors.results,
            clear: container.dataset.clearSelector || config.selectors.clear,
            id: container.dataset.idSelector || config.selectors.id,
            view: container.dataset.viewSelector || config.selectors.view,
            item: container.dataset.itemSelector || config.selectors.item
        };
    }

    /**
     * Initialize all lookup fields on the page
     */
    function init() {
        document.querySelectorAll(config.selectors.container).forEach(container => {
            initializeLookup(container);
        });

        // Listen for HTMX events
        document.body.addEventListener('htmx:afterSwap', function(evt) {
            const resultsSelector = config.selectors.results.replace('.', '');
            if (evt.detail.target.classList.contains(resultsSelector)) {
                showResults(evt.detail.target);
            }
        });
    }

    /**
     * Initialize a single lookup field
     */
    function initializeLookup(container) {
        const selectors = getSelectors(container);
        const searchInput = container.querySelector(selectors.search);
        const clearBtn = container.querySelector(selectors.clear);
        const resultsDiv = container.querySelector(selectors.results);

        if (!searchInput) return;

        // Search input events
        searchInput.addEventListener('input', function(e) {
            handleSearchInput(container, e.target.value);
        });

        searchInput.addEventListener('focus', function(e) {
            if (e.target.value.length >= config.minSearchLength) {
                showResults(resultsDiv);
            }
        });

        searchInput.addEventListener('keydown', function(e) {
            handleKeydown(container, e);
        });

        // Clear button
        if (clearBtn) {
            clearBtn.addEventListener('click', function() {
                clearLookup(container);
            });
        }

        // Click outside to close
        document.addEventListener('click', function(e) {
            if (!container.contains(e.target)) {
                hideResults(resultsDiv);
            }
        });
    }

    /**
     * Handle search input changes
     */
    function handleSearchInput(container, value) {
        const selectors = getSelectors(container);
        const resultsDiv = container.querySelector(selectors.results);
        const idInput = container.querySelector(selectors.id);

        // Clear timeout
        if (searchTimeout) {
            clearTimeout(searchTimeout);
        }

        // Clear ID if search text changes
        if (idInput && idInput.value) {
            idInput.value = '';
            container.classList.remove('sf-lookup-selected');
        }

        if (value.length < config.minSearchLength) {
            hideResults(resultsDiv);
            return;
        }

        // Delay search
        searchTimeout = setTimeout(() => {
            triggerSearch(container);
        }, config.searchDelay);
    }

    /**
     * Trigger HTMX search
     */
    function triggerSearch(container) {
        const selectors = getSelectors(container);
        const searchInput = container.querySelector(selectors.search);
        if (searchInput) {
            htmx.trigger(searchInput, 'keyup');
        }
    }

    /**
     * Handle keyboard navigation
     */
    function handleKeydown(container, event) {
        const selectors = getSelectors(container);
        const resultsDiv = container.querySelector(selectors.results);
        const items = resultsDiv.querySelectorAll(selectors.item);

        if (items.length === 0) return;

        const current = resultsDiv.querySelector(selectors.item + '.active');
        let index = current ? Array.from(items).indexOf(current) : -1;

        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                index = Math.min(index + 1, items.length - 1);
                setActiveItem(items, index);
                break;

            case 'ArrowUp':
                event.preventDefault();
                index = Math.max(index - 1, 0);
                setActiveItem(items, index);
                break;

            case 'Enter':
                event.preventDefault();
                if (current) {
                    selectItem(current);
                }
                break;

            case 'Escape':
                hideResults(resultsDiv);
                break;
        }
    }

    /**
     * Set active item in dropdown
     */
    function setActiveItem(items, index) {
        items.forEach((item, i) => {
            item.classList.toggle('active', i === index);
            if (i === index) {
                item.scrollIntoView({ block: 'nearest' });
            }
        });
    }

    /**
     * Select a lookup item
     */
    function selectItem(element) {
        const container = element.closest(config.selectors.container);
        if (!container) return;

        const selectors = getSelectors(container);
        const id = element.dataset.id;
        const name = element.dataset.name;
        const objectType = element.dataset.object;

        const idInput = container.querySelector(selectors.id);
        const searchInput = container.querySelector(selectors.search);
        const resultsDiv = container.querySelector(selectors.results);
        const viewBtn = container.querySelector(selectors.view);

        if (idInput) idInput.value = id;
        if (searchInput) searchInput.value = name;

        // Update view button href
        if (viewBtn && objectType) {
            viewBtn.href = `/Salesforce/${objectType}/Details/${id}`;
            viewBtn.style.display = '';
        }

        container.classList.add('sf-lookup-selected');
        hideResults(resultsDiv);

        // Flash effect
        searchInput.classList.add('sf-lookup-flash');
        setTimeout(() => {
            searchInput.classList.remove('sf-lookup-flash');
        }, 500);

        // Trigger change event for dependent lookups
        if (idInput) {
            idInput.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    /**
     * Clear a lookup field
     */
    function clearLookup(container) {
        const selectors = getSelectors(container);
        const idInput = container.querySelector(selectors.id);
        const searchInput = container.querySelector(selectors.search);
        const resultsDiv = container.querySelector(selectors.results);
        const viewBtn = container.querySelector(selectors.view);

        if (idInput) idInput.value = '';
        if (searchInput) {
            searchInput.value = '';
            searchInput.focus();
        }
        if (viewBtn) viewBtn.style.display = 'none';

        container.classList.remove('sf-lookup-selected');
        hideResults(resultsDiv);

        // Trigger change for dependent lookups
        if (idInput) {
            idInput.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    /**
     * Show results dropdown
     */
    function showResults(resultsDiv) {
        if (resultsDiv && resultsDiv.innerHTML.trim()) {
            resultsDiv.classList.add('show');
        }
    }

    /**
     * Hide results dropdown
     */
    function hideResults(resultsDiv) {
        if (resultsDiv) {
            resultsDiv.classList.remove('show');
        }
    }

    /**
     * Setup dependent lookup relationship
     */
    function setupDependentLookup(childFieldName, parentFieldName, filterField) {
        const childContainer = document.querySelector(`[data-field-name="${childFieldName}"]`);
        const parentContainer = document.querySelector(`[data-field-name="${parentFieldName}"]`);

        if (!childContainer || !parentContainer) return;

        const parentSelectors = getSelectors(parentContainer);
        const childSelectors = getSelectors(childContainer);
        const parentIdInput = parentContainer.querySelector(parentSelectors.id);
        const childSearchInput = childContainer.querySelector(childSelectors.search);

        if (parentIdInput && childSearchInput) {
            parentIdInput.addEventListener('change', function() {
                // Clear child when parent changes
                clearLookup(childContainer);

                // Update child HTMX attributes with parent filter
                if (this.value) {
                    childSearchInput.setAttribute('hx-vals', JSON.stringify({
                        targetObject: childSearchInput.dataset.target,
                        parentField: filterField,
                        parentValue: this.value
                    }));
                }
            });
        }
    }

    /**
     * Configure the lookup module
     * @param {Object} options - Configuration options
     */
    function configure(options) {
        if (options.minSearchLength !== undefined) {
            config.minSearchLength = options.minSearchLength;
        }
        if (options.searchDelay !== undefined) {
            config.searchDelay = options.searchDelay;
        }
        if (options.lookupSearchUrl !== undefined) {
            config.lookupSearchUrl = options.lookupSearchUrl;
        }
        if (options.selectors) {
            Object.assign(config.selectors, options.selectors);
        }
    }

    // Public API
    return {
        init: init,
        selectItem: selectItem,
        clearLookup: clearLookup,
        setupDependentLookup: setupDependentLookup,
        configure: configure,
        config: config
    };
})();

// Auto-initialize on DOM ready
document.addEventListener('DOMContentLoaded', SalesforceLookup.init);

// Re-initialize after HTMX swaps
document.body.addEventListener('htmx:afterSettle', function() {
    SalesforceLookup.init();
});
