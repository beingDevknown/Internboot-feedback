/**
 * Ads Management Script
 * This script handles the initialization, display, and interaction with ads throughout the application.
 * PRODUCTION VERSION - REAL ADS ONLY
 */

// Wait for DOM to be fully loaded
document.addEventListener('DOMContentLoaded', function() {
    console.log('Ads script loaded - PRODUCTION VERSION (Real Ads Only)');

    // Log AdSense status
    if (typeof adsbygoogle !== 'undefined') {
        console.log('AdSense object found and ready');
    } else {
        console.warn('AdSense object not found - this may indicate a blocker or script loading issue');
    }

    // Initialize all ads
    initializeAds();

    // Make ads clickable
    makeAdsClickable();

    // Set up popup ads if enabled
    setupPopupAds();
});

/**
 * Initialize all AdSense ads
 */
function initializeAds() {
    console.log('Initializing AdSense ads - PRODUCTION MODE');
    try {
        // PRODUCTION MODE: Always use real ads
        const shouldShowRealAds = true;

        // Set this in the AdConfig for other functions to use
        if (window.AdConfig) {
            window.AdConfig.shouldShowRealAds = true;
            window.AdConfig.useFallbackOnly = false;
        }

        console.log('Ad configuration - PRODUCTION MODE:', {
            enabled: true,
            shouldShowRealAds: true,
            publisherId: window.AdConfig?.publisherId || 'ca-pub-8504842908769623',
            adSlot: window.AdConfig?.slots?.rectangle || '9319968119'
        });

        // Try to initialize AdSense for production
        if (typeof adsbygoogle !== 'undefined') {
            console.log('AdSense object found, pushing ads...');
            // AdSense is loaded, push ads
            document.querySelectorAll('ins.adsbygoogle:not(.adsbygoogle-initialized)').forEach(function(ad, index) {
                console.log(`Initializing ad ${index + 1}...`);
                try {
                    // Get the publisher ID and ad slot
                    let publisherId = ad.getAttribute('data-ad-client');
                    let adSlot = ad.getAttribute('data-ad-slot');

                    // If publisher ID is missing or has placeholder, use the one from config
                    if (!publisherId || publisherId.includes('XXXXXXX')) {
                        publisherId = window.AdConfig?.publisherId || 'ca-pub-8504842908769623';
                        ad.setAttribute('data-ad-client', publisherId);
                        console.log(`Updated publisher ID for ad ${index + 1} to ${publisherId}`);
                    }

                    // If ad slot is missing or has placeholder, use the one from config
                    if (!adSlot || adSlot.includes('XXXXXXX') || adSlot === '1234567890') {
                        // Get the ad type from the element or default to rectangle
                        const adType = ad.getAttribute('data-ad-type') || 'rectangle';
                        adSlot = window.AdConfig?.slots?.[adType] || '9319968119';
                        ad.setAttribute('data-ad-slot', adSlot);
                        console.log(`Updated ad slot for ad ${index + 1} to ${adSlot} (type: ${adType})`);
                    }

                    // Initialize the ad with real values
                    console.log(`Initializing real ad ${index + 1} with slot: ${adSlot}`);
                    (adsbygoogle = window.adsbygoogle || []).push({});
                    ad.classList.add('adsbygoogle-initialized');
                } catch (e) {
                    console.error(`Error initializing ad ${index + 1}:`, e);
                }
            });
        } else {
            console.warn('AdSense object not found, showing fallback content...');
            // AdSense not loaded, show fallback content
            document.querySelectorAll('.ad-fallback').forEach(function(fallback) {
                fallback.style.display = 'flex';
            });
        }
    } catch (e) {
        console.error('Error initializing ads:', e);
        // Show fallback content on error
        document.querySelectorAll('.ad-fallback').forEach(function(fallback) {
            fallback.style.display = 'flex';
        });
    }
}

/**
 * Make ads clickable
 */
function makeAdsClickable() {
    console.log('Making ads clickable...');
    const clickableAds = document.querySelectorAll('.ad-clickable');

    clickableAds.forEach((ad, index) => {
        console.log(`Setting up clickable ad ${index + 1}...`);

        // Add click event to the container
        ad.addEventListener('click', function(e) {
            // Only handle clicks on the container or fallback content, not on child elements
            if (e.target === ad || e.target.closest('.ad-fallback')) {
                console.log('Ad clicked, attempting to find link...');

                // First try to find a link in the fallback content
                const fallbackLink = ad.querySelector('.ad-fallback a');
                if (fallbackLink) {
                    console.log('Fallback link found, opening...');
                    window.open(fallbackLink.href, '_blank');
                    return;
                }

                // If no fallback link, use the default ad link
                const defaultLink = ad.getAttribute('data-ad-link');
                if (defaultLink) {
                    console.log('Default link found, opening...');
                    window.open(defaultLink, '_blank');
                    return;
                }

                // If no links found, use a default sponsor link
                console.log('No links found, using default sponsor link...');
                window.open('https://www.example.com/sponsors', '_blank');
            }
        });
    });
}

/**
 * In production mode, we don't use fallback content
 * This function is kept for compatibility but doesn't do anything
 */
function ensureFallbackVisibility() {
    console.log('Fallback visibility check disabled in production mode');
    // Do nothing in production mode - we only want real ads
}

/**
 * In production mode, we don't use fallback content
 * This function is kept for compatibility but doesn't do anything
 */
function checkAdVisibility(container, index, type) {
    // Do nothing in production mode - we only want real ads
}

/**
 * Set up popup ads
 */
function setupPopupAds() {
    console.log('Setting up popup ads...');

    // Only show popup ads to non-premium users
    if (document.body.getAttribute('data-user-premium') === 'true') {
        console.log('Premium user detected, skipping popup ads');
        return;
    }

    const popupAdContainer = document.getElementById('popupAdContainer');
    if (!popupAdContainer) {
        console.log('No popup ad container found');
        return;
    }

    // Check if this is a test page
    const isTestPage = popupAdContainer.getAttribute('data-is-test-page') === 'true';
    console.log('Is test page:', isTestPage);

    // Set the appropriate delay based on the page type and configuration
    const initialDelay = isTestPage ?
        (window.AdConfig?.timing?.testPageInitialPopupDelay || 10 * 60 * 1000) :
        (window.AdConfig?.timing?.regularPagePopupDelay || 120 * 1000); // Use config values or defaults
    console.log(`Setting initial popup delay to ${initialDelay / 1000} seconds (${initialDelay / 1000 / 60} minutes)`);

    // Function to show the popup ad
    function showPopupAd() {
        console.log('Showing popup ad...');

        // Check if user has recently seen a popup ad
        if (hasRecentlySeenPopupAd()) {
            console.log('Skipping popup ad - user has recently seen one');
            return;
        }

        // PRODUCTION MODE: Always use real ads
        const shouldShowRealAds = true;

        // Initialize the ad if not already initialized
        const adElement = popupAdContainer.querySelector('ins.adsbygoogle');
        if (adElement && !adElement.classList.contains('adsbygoogle-initialized')) {
            // Get the ad slot
            let adSlot = adElement.getAttribute('data-ad-slot');
            let publisherId = adElement.getAttribute('data-ad-client');

            // If publisher ID is missing or has placeholder, use the one from config
            if (!publisherId || publisherId.includes('XXXXXXX')) {
                publisherId = window.AdConfig?.publisherId || 'ca-pub-8504842908769623';
                adElement.setAttribute('data-ad-client', publisherId);
                console.log(`Updated popup ad publisher ID to ${publisherId}`);
            }

            // If ad slot is missing or has placeholder, use the one from config
            if (!adSlot || adSlot.includes('XXXXXXX') || adSlot === '1234567890') {
                adSlot = window.AdConfig?.slots?.popup || '9319968119';
                adElement.setAttribute('data-ad-slot', adSlot);
                console.log(`Updated popup ad slot to ${adSlot}`);
            }

            try {
                console.log('Initializing popup AdSense ad with slot:', adSlot);
                (adsbygoogle = window.adsbygoogle || []).push({});
                adElement.classList.add('adsbygoogle-initialized');

                // Set a timeout to check if the ad loaded properly
                setTimeout(() => {
                    const adIframe = adElement.querySelector('iframe');
                    if (!adIframe || adIframe.clientHeight < 10) {
                        console.log('AdSense popup ad did not load properly');
                    } else {
                        console.log('AdSense popup ad loaded successfully');
                    }
                }, 2000); // Check after 2 seconds
            } catch (e) {
                console.error('Error initializing popup ad:', e);
            }
        } else if (adElement) {
            console.log('Popup ad already initialized');
        } else {
            console.error('No popup ad element found');
        }

        // Always display the popup container
        popupAdContainer.style.display = 'flex';

        // Set up recurring popups for test pages
        if (isTestPage) {
            // Schedule the next popup using the configured delay
            const recurringDelay = window.AdConfig?.timing?.testPageRecurringPopupDelay || 5 * 60 * 1000;
            console.log(`Scheduling next popup in ${recurringDelay / 1000} seconds (${recurringDelay / 1000 / 60} minutes)`);
            setTimeout(showPopupAd, recurringDelay);
        }
    }

    // In production mode, we don't use fallback content
    // This function is kept for compatibility but doesn't do anything
    function showFallbackAd() {
        console.log('Fallback content disabled in production mode');
        // Do nothing - we want to show real ads only
    }

    // Add close button functionality with session tracking
    const closeButton = popupAdContainer.querySelector('.popup-ad-close');
    if (closeButton) {
        closeButton.addEventListener('click', function(e) {
            e.preventDefault();
            e.stopPropagation();

            // Hide the popup
            popupAdContainer.style.display = 'none';

            // Store in session that user has seen a popup ad
            // This helps prevent showing too many popups in a single session
            sessionStorage.setItem('popupAdShown', 'true');

            // Record the time when the popup was closed
            sessionStorage.setItem('lastPopupAdTime', Date.now().toString());

            console.log('Popup ad closed by user');
        });
    }

    // Check if user has recently seen a popup ad
    // This helps comply with Google's policies on frequency
    function hasRecentlySeenPopupAd() {
        const lastPopupTime = sessionStorage.getItem('lastPopupAdTime');
        if (!lastPopupTime) return false;

        // Don't show another popup for at least 5 minutes after the last one
        const minTimeBetweenPopups = 5 * 60 * 1000; // 5 minutes
        const timeSinceLastPopup = Date.now() - parseInt(lastPopupTime);

        return timeSinceLastPopup < minTimeBetweenPopups;
    }

    // Make the popup ad clickable
    const popupAd = popupAdContainer.querySelector('.popup-ad');
    if (popupAd) {
        popupAd.addEventListener('click', function(e) {
            // Don't trigger if clicking the close button
            if (e.target.closest('.popup-ad-close')) {
                return;
            }

            // Check if we're clicking on an AdSense iframe
            if (e.target.tagName === 'IFRAME' && e.target.closest('ins.adsbygoogle')) {
                // Let AdSense handle the click
                return;
            }

            // Otherwise use our fallback link
            const adLink = popupAd.getAttribute('data-ad-link');
            if (adLink) {
                window.open(adLink, '_blank');
            }
        });
    }

    // Show the first popup after the initial delay
    setTimeout(showPopupAd, initialDelay);

    // Show popup ad after a reasonable delay to comply with Google AdSense policies
    // Google AdSense policies discourage immediate popups when a user lands on a page
    console.log('Scheduling popup ad with 10-second delay to comply with AdSense policies');

    // Use the configured delay from ad-config.js
    const policyCompliantDelay = window.AdConfig?.timing?.regularPagePopupDelay || 10000;

    // Show popup after the policy-compliant delay, but only if user hasn't recently seen one
    setTimeout(() => {
        // Check if user has recently seen a popup ad
        if (!hasRecentlySeenPopupAd() && !sessionStorage.getItem('popupAdShown')) {
            console.log('Showing popup ad after delay');
            showPopupAd();
        } else {
            console.log('Skipping popup ad - user has recently seen one');
        }
    }, policyCompliantDelay); // Show after configured delay (default: 10 seconds)
}
