# Local and AWS share PostgreSQL through Systems Manager

The local launcher supports explicit `-DatabaseMode Local` and `-DatabaseMode
SharedAws`. Once verified, this installation records its selection in the ignored
workspace `.tools/local-database-mode.txt` file. The presence of a credential file
alone never changes the selected database. Demo mode always stays isolated.

SharedAws connects to PostgreSQL database `webgso` on EC2 instance
`i-0169c2d5a6674e1ea`, ap-southeast-5, using the Session Manager plugin and the
AWS-StartPortForwardingSession document. Only loopback port 15439 is opened on the
laptop, forwarding to server loopback port 5432. No SSH, PEM key, public database
port or security-group change is used. A changed AWS public IP does not affect
this connection because it targets the instance ID.

Credentials are loaded at runtime from the existing DPAPI-encrypted
`.tools/shared-aws-database.credential.xml` in the workspace. Never print, copy to
source control, or expose the decrypted password. A valid AWS CLI login for
account 441798856671, internet access and an Online SSM agent are required.

Start local with `run-java.cmd -Port 5187`. If a session expires or the laptop
sleeps, restart the launcher; it reuses a recognized live SSM tunnel or creates a
new one, and rejects any unknown process occupying port 15439. A failed AWS
connection does not silently fall back to the old laptop database.

Shared local startup disables schema initialization, bootstrap accounts, demo
replacement, stale-test-claim cleanup, workers, local backups, mail dispatch,
alerts and digests. Existing schema is used as-is. These settings do not change
the AWS service environment. Local binds to loopback and uses a separate HTTP
session cookie. The shared connection pool is limited to three connections.

User actions from either site read and change the same data. Deleting an account
or plant through local therefore affects AWS too. Old laptop-only accounts/data
are not imported. The laptop database is kept unchanged.

To use the laptop database deliberately, stop the local application and run
`run-java.cmd -DatabaseMode Local -Port 5187`. The mode file can also be changed to
Local. No database restoration or copying is part of switching the connection.

Verified 2026-11-16: 227 tests passed, local /login returned HTTP 200, and AWS pg_stat_activity showed three WebGSO-local-shared connections alongside ten AWS JDBC connections in webgso. The SharedAws mode was saved only after this confirmation. The old laptop database was not imported or removed.
