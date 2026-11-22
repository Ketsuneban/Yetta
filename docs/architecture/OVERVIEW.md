# Yetta architecture — 0.7.0

| Component | Responsibility |
|---|---|
| Tallow | Core coordination, lifecycle, update and notification orchestration, dispatching child module work |
| DAX | Access policy, service grant validation, process observation and security decisions |
| Yetta | WPF interface and decision presentation |
| Marvine | Console input, completion, history and rendering |
| Child modules | Subsy sessions, package management and other bounded operations |

The implementation shares pure logic in Yetta.Core. Desktop adapters remain in Yetta.Desktop partial classes, including MainWindow.Tallow.cs. DaxPolicy owns the common operation matrix. The LocalService component stores the Administrator grant; it is not a privileged execution broker.

Guest/User are logical profiles. Administrator requires a verified service grant. System is unavailable. Python inherits the actual Windows token. Process observation is passive polling with documented gaps. Recovery is unchanged.

See SECURITY.md for current boundaries. Historical decisions in TALLOW-DAX-DECISIONS.md describe future product goals and are not implementation claims.
