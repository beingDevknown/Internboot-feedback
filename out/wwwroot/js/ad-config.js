/**
 * Ad Configuration
 * This file contains configuration settings for ads throughout the application.
 *
 * PRODUCTION CONFIGURATION - REAL ADS ONLY
 */

const AdConfig = {
    // Set to true to enable ads in production, false to disable all ads
    enabled: true,

    // IMPORTANT: Set to false to show real ads, true to force fallback content
    useFallbackOnly: false,

    // Enable real ads even in development environment
    enableInDevelopment: true,

    // Your AdSense publisher ID - REAL, VERIFIED PUBLISHER ID
    publisherId: 'ca-pub-8504842908769623',

    // Ad slot IDs - these are real ad unit IDs from your AdSense account
    // IMPORTANT: All slots use the same ID to ensure consistency
    slots: {
        banner: '9319968119',      // Regular banner ads (internboot)
        rectangle: '9319968119',   // Rectangle ads (internboot)
        popup: '9319968119',       // Popup ads (internboot)
        test: '9319968119'         // Ads shown during tests (internboot)
    },

    // Timing configuration (in milliseconds)
    timing: {
        // Time before showing popup ads on regular pages - set to 10 seconds
        regularPagePopupDelay: 10 * 1000,  // 10 seconds

        // Time before showing the first popup ad during a test - set to 3 minutes
        testPageInitialPopupDelay: 3 * 60 * 1000,  // 3 minutes

        // Time between popup ads during a test - set to 10 minutes to avoid disrupting test takers
        testPageRecurringPopupDelay: 10 * 60 * 1000  // 10 minutes
    },

    // Fallback content configuration
    fallback: {
        // Default link for fallback ads
        defaultLink: 'https://internboot.com',

        // Titles for different types of fallback ads
        titles: {
            regular: 'Boost Your Career',
            popup: 'Premium Learning Resources'
        },

        // Messages for different types of fallback ads
        messages: {
            regular: 'Explore premium courses and certification programs to advance your career!',
            popup: 'Get access to exclusive study materials and practice tests!'
        }
    }
};

// Don't modify below this line
// Check if we're in a development environment
AdConfig.isDevelopment = window.location.hostname === 'localhost' ||
                        window.location.hostname === '127.0.0.1' ||
                        window.location.hostname.includes('.local');

// Check if we should use placeholder values
AdConfig.hasPlaceholderValues = AdConfig.publisherId.includes('XXXXXXX');

// Determine if we should show real ads
AdConfig.shouldShowRealAds = AdConfig.enabled &&
                            (!AdConfig.isDevelopment || AdConfig.enableInDevelopment) &&
                            !AdConfig.useFallbackOnly &&
                            !AdConfig.hasPlaceholderValues;

// Export the configuration
window.AdConfig = AdConfig;
