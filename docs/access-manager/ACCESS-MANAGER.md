# Access Manager — current preview

Guest and User are logical application profiles. Safe User was merged into Guest. Administrator is an installed grant maintained by the LocalService component; it does not elevate arbitrary scripts. System remains unavailable.

DaxPolicy defines the common operation matrix. Read operations, application updates and stopping owned scripts remain available under Guest. Package mutation requires User or Administrator. Python launch requires Administrator and a Ready DAX companion, with a fresh service status check before execution.

Initial installation uses real Windows UAC. Grant ownership belongs to the account approving installation. Revoke persists across service restarts and requires reinstallation to restore the grant. Each component retains the real Windows token and account identity.
