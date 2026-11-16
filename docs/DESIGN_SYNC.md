# Automatic AWS updates without SSH

Run `start-design-sync.cmd` to watch saved files; `stop-design-sync.cmd` stops after
any in-progress deployment completes. The existing Windows login shortcut starts
the watcher when you sign in. The laptop must remain on and online.

The watcher covers `src/main` and `pom.xml`, including Java code, Java views and
static assets. After 20 quiet seconds it snapshots the source, runs Maven verify,
uploads the tested JAR using encrypted S3 objects, and deploys using AWS Systems
Manager. It uses instance ID i-0169c2d5a6674e1ea in ap-southeast-5, so a changed
public IP does not break deployment. No SSH, SCP or PEM key is used.

Build failures leave AWS untouched. Saves during a build discard the stale build
and queue the latest source. The existing deployment helper backs up the current
JAR and restores it if startup/login checks fail. Server credentials and database
contents are not copied from local. Normal application startup still runs its
existing startup code. This does not synchronize the two databases or change
mail configuration. The local running JAR is not replaced by this watcher.

AWS CLI must have a valid login for account 441798856671. Run AWS CLI login when
credentials expire, then restart sync. No permanent access keys are saved by these
scripts. Unattended operation after the login expires requires a separate CI/CD
identity; it is not configured here. Any IAM errors stop before a new release is
activated. The account needs EC2 DescribeInstances, SSM SendCommand and
GetCommandInvocation, and S3 PutObject/GetObject/DeleteObject for the
`webgso-auto-deploy/` prefix in gso-prototype-data-my-k7m4p2.

Status: `.tools/design-sync/status.log`. Build/deploy output:
`.tools/design-sync/last-deploy.log`. After a failure, correct/save the source or
restart sync to retry. Temporary S3 objects are removed after the remote command
finishes; an indeterminate remote status keeps them for diagnosis. A deployment
restarts WebGSO and can sign out active users. Refresh the browser after success.
