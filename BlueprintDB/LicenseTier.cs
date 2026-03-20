namespace Blueprint.App;

public enum LicenseTier { Free, Pro }

public enum LicenseActivationResult { Success, InvalidKey, AlreadyActive, NetworkError, KeyExhausted }
