namespace Deskbridge.Core.Models;

public enum Protocol { Rdp, Ssh, Vnc }
public enum CredentialMode { Inherit, Own, Prompt }
public enum DisconnectReason { UserInitiated, RemoteDisconnect, Error, AppShutdown }
public enum ConnectionQuality { Excellent, Good, Fair, Poor, Unknown }
public enum LockReason { Manual, Timeout, SessionSwitch, Minimise }
public enum AuditAction
{
    Connected, Disconnected, FailedConnect, Reconnected,
    ConnectionCreated, ConnectionEdited, ConnectionDeleted,
    ConnectionsImported, ConnectionsExported,
    CredentialStored, CredentialDeleted,
    AppStarted, AppClosed, UpdateApplied,
    AppLocked, AppUnlocked, MasterPasswordChanged
}
