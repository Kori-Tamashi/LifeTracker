import Testing

@testable import LifeTrackerClient

struct RuntimePreferencesTests {
    @Test func betaCandidatesCannotRedirectOlderBetaOrDevelopmentPreferences() {
        let first = RuntimePreferencesKeys(channel: "beta", version: "0.1.0-beta.1")
        let second = RuntimePreferencesKeys(channel: "beta", version: "0.1.0-beta.2")
        let third = RuntimePreferencesKeys(channel: "beta", version: "0.1.0-beta.3")
        let development = RuntimePreferencesKeys(channel: nil, version: nil)
        #expect(first.root == "betaRuntimeRoot" && first.port == "betaRuntimePort")
        #expect(Set([first.root, second.root, third.root, development.root]).count == 4)
        #expect(Set([first.port, second.port, third.port, development.port]).count == 4)
        #expect(Set([first.backup, second.backup, third.backup, development.backup]).count == 4)
        #expect(development.root == "runtimeRoot" && development.port == "runtimePort")
    }
}
