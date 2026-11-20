/// Beta candidates use separate preferences so testing cannot redirect the installed beta.1 profile.
public struct RuntimePreferencesKeys: Sendable {
    public let root: String
    public let port: String
    public let backup: String

    public init(channel: String?, version: String?) {
        if channel == "beta", let version {
            if version == "0.1.0-beta.1" {
                root = "betaRuntimeRoot"
                port = "betaRuntimePort"
                backup = "betaAutomaticBackupRoot"
            } else {
                root = "beta.\(version).runtimeRoot"
                port = "beta.\(version).runtimePort"
                backup = "beta.\(version).automaticBackupRoot"
            }
        } else {
            root = "runtimeRoot"
            port = "runtimePort"
            backup = "automaticBackupRoot"
        }
    }
}
