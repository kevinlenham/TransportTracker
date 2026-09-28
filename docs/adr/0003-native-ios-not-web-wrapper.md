# iOS app will be native SwiftUI, not a wrapped PWA

The web client is a React + TypeScript PWA, and the planned iOS app is a separate SwiftUI client that uses the same API, rather than a Capacitor or WebView wrapper around the PWA. Apple often rejects thin web wrappers (App Store guideline 4.2), and a native client is worth more in a portfolio. The consequence is that all business logic, including the stats, must live in the API rather than the React client, so that both clients show the same numbers.
