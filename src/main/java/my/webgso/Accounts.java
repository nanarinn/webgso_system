package my.webgso;

import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.authentication.*;
import org.springframework.security.core.*;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.userdetails.*;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class Accounts implements AuthenticationProvider {
  public static final String ADMIN = "Admin";
  // Read former assignments for existing accounts without rewriting the shared database.
  private static final Set<String> COMPATIBLE_ADMIN_ROLES =
      Set.of(ADMIN, "Staff", "Administrator", "SuperAdministrator");

  public record Principal(String id, String username, String displayName, String stamp)
      implements java.io.Serializable {}

  private final Store db;
  private final IdentityPasswordEncoder passwords = new IdentityPasswordEncoder();

  public Accounts(Store db) {
    this.db = db;
  }

  public boolean canReview(Authentication auth) {
    if (auth == null
        || !(auth.getPrincipal() instanceof Principal principal)
        || !auth.getAuthorities().contains(new SimpleGrantedAuthority("ROLE_" + ADMIN)))
      return false;
    var user = db.find(ApplicationUser.class, principal.id()).orElse(null);
    return user != null
        && "Approved".equals(user.accessStatus)
        && Objects.equals(principal.stamp(), user.securityStamp)
        && isReviewer(user.id);
  }

  private boolean isReviewer(String userId) {
    return db.jdbc()
            .queryForObject(
                "SELECT COUNT(*) FROM \"GsoAccessReviewer\" WHERE \"Id\"=1 AND \"UserId\"=?",
                Long.class,
                userId)
        == 1;
  }

  public List<String> roles(String userId) {
    var assigned =
        db.jdbc()
            .queryForList(
                "SELECT r.\"Name\" FROM \"AspNetRoles\" r JOIN \"AspNetUserRoles\" ur ON"
                    + " r.\"Id\"=ur.\"RoleId\" WHERE ur.\"UserId\"=?",
                String.class,
                userId);
    return assigned.stream().anyMatch(COMPATIBLE_ADMIN_ROLES::contains)
        ? List.of(ADMIN)
        : List.of();
  }

  public Authentication token(ApplicationUser user) {
    return UsernamePasswordAuthenticationToken.authenticated(
        new Principal(user.id, user.userName, displayName(user.displayName), user.securityStamp),
        null,
        roles(user.id).stream().map(r -> new SimpleGrantedAuthority("ROLE_" + r)).toList());
  }

  public static String displayName(String name) {
    return "Demo Super Admin".equals(name) ? "Demo Admin" : name;
  }

  @Override
  @Transactional(noRollbackFor = AuthenticationException.class)
  public Authentication authenticate(Authentication request) throws AuthenticationException {
    String login = request.getName().trim().toUpperCase(Locale.ROOT);
    var users =
        db.list(
            ApplicationUser.class,
            "WHERE \"NormalizedUserName\"=? OR \"NormalizedEmail\"=? FOR UPDATE",
            login,
            login);
    if (users.isEmpty()) {
      passwords.matches(request.getCredentials().toString(), DUMMY);
      throw new BadCredentialsException("Invalid credentials");
    }
    var user = users.getFirst();
    if (user.lockoutEnd != null && user.lockoutEnd.isAfter(OffsetDateTime.now()))
      throw new LockedException("Account temporarily locked");
    if (!passwords.matches(request.getCredentials().toString(), user.passwordHash)) {
      user.accessFailedCount = (user.accessFailedCount == null ? 0 : user.accessFailedCount) + 1;
      if (user.accessFailedCount >= 5) {
        user.lockoutEnd = OffsetDateTime.now(ZoneOffset.UTC).plusMinutes(15);
        user.accessFailedCount = 0;
      }
      db.update(user);
      throw new BadCredentialsException("Invalid credentials");
    }
    if (!"Approved".equals(user.accessStatus))
      throw new DisabledException("This account is pending approval or inactive");
    user.accessFailedCount = 0;
    user.lockoutEnd = null;
    if (passwords.upgradeEncoding(user.passwordHash))
      user.passwordHash = passwords.encode(request.getCredentials().toString());
    db.update(user);
    audit(user.userName, "Sign in", "User", user.id, "Success", "Local authentication");
    return token(user);
  }

  private static final String DUMMY =
      new IdentityPasswordEncoder().encode(UUID.randomUUID().toString());

  @Override
  public boolean supports(Class<?> type) {
    return UsernamePasswordAuthenticationToken.class.isAssignableFrom(type);
  }

  @Transactional
  public ApplicationUser register(String name, String email, String displayName, String password) {
    if (!name.matches("[a-zA-Z0-9_.@-]{3,100}")
        || !email.matches("[^\\s@]+@[^\\s@]+\\.[^\\s@]+")
        || email.length() > 256
        || displayName.isBlank()
        || displayName.length() > 160)
      throw new IllegalArgumentException("Enter a valid username, email and display name.");
    if (password.length() < 12
        || password.length() > 72
        || !password.matches("(?s).*[A-Z].*")
        || !password.matches("(?s).*[a-z].*")
        || !password.matches("(?s).*\\d.*")
        || password.chars().allMatch(Character::isLetterOrDigit))
      throw new IllegalArgumentException(
          "Use 12–72 characters including uppercase, lowercase, a number and a symbol.");
    if (!db.list(
            ApplicationUser.class,
            "WHERE \"NormalizedUserName\"=? OR \"NormalizedEmail\"=?",
            name.toUpperCase(Locale.ROOT),
            email.toUpperCase(Locale.ROOT))
        .isEmpty())
      throw new IllegalArgumentException("The username or email is already registered.");
    var user = new ApplicationUser();
    user.id = UUID.randomUUID().toString();
    user.userName = name;
    user.normalizedUserName = name.toUpperCase(Locale.ROOT);
    user.email = email;
    user.normalizedEmail = email.toUpperCase(Locale.ROOT);
    user.displayName = displayName;
    user.passwordHash = passwords.encode(password);
    user.securityStamp = UUID.randomUUID().toString();
    user.concurrencyStamp = UUID.randomUUID().toString();
    user.accessRequestedAt = OffsetDateTime.now(ZoneOffset.UTC);
    user.accessStatus = "Pending";
    db.insert(user);
    setRole(user.id, ADMIN);
    audit(name, "Account created", "User", user.id, "Success", "Local account registration");
    for (var approver : db.list(ApplicationUser.class, "WHERE \"AccessStatus\"='Approved'")) {
      if (isReviewer(approver.id) && roles(approver.id).contains(ADMIN))
        queueNotification(
            approver.id,
            user.id,
            "GSO Web Services - New account approval required",
            "A new user has requested access.\nName: "
                + displayName
                + "\nEmail: "
                + email
                + "\n\n"
                + "Sign in to GSO Web Services and open Access requests to approve or reject this"
                + " account. The user cannot sign in until approved.",
            "AccessRequest");
    }
    queueNotification(
        user.id,
        user.id,
        "GSO Web Services - Access request received",
        "Your account request has been received and is pending Admin approval. You cannot sign in"
            + " yet. We will email you when your request is approved or rejected.",
        "AccessDecision");
    return user;
  }

  @Transactional
  public void review(
      Authentication actor, String targetId, String decision, String requestedRole, String note) {
    if (!canReview(actor))
      throw new AccessDeniedException(
          "Only the designated account reviewer can manage access requests.");
    Principal reviewer = (Principal) actor.getPrincipal();
    var target =
        db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", targetId).stream()
            .findFirst()
            .orElseThrow();
    if (targetId.equals(reviewer.id))
      throw new IllegalArgumentException("Ask another Admin to review your account.");
    if (!Set.of("Approved", "Rejected", "Inactive").contains(decision)
        || !ADMIN.equals(requestedRole))
      throw new IllegalArgumentException("Invalid access decision");
    if (decision.equals(target.accessStatus)) return;
    target.accessStatus = decision;
    target.accessReviewedAt = OffsetDateTime.now(ZoneOffset.UTC);
    target.accessReviewedBy = reviewer.username;
    target.accessReviewNote = note;
    target.securityStamp = UUID.randomUUID().toString();
    db.update(target);
    if (decision.equals("Approved")) setRole(targetId, requestedRole);
    audit(reviewer.username, "Access " + decision, "User", targetId, "Success", note);
    notify(
        targetId,
        reviewer.id,
        "Access " + decision,
        "Your GSO Web Services access request is "
            + decision.toLowerCase(Locale.ROOT)
            + ". "
            + note);
  }

  public void setRole(String id, String role) {
    if (!ADMIN.equals(role))
      throw new IllegalArgumentException("Admin is the only supported role.");
    var ids =
        db.jdbc()
            .queryForList(
                "SELECT \"Id\" FROM \"AspNetRoles\" WHERE \"Name\"=?", String.class, role);
    String roleId = ids.isEmpty() ? UUID.randomUUID().toString() : ids.getFirst();
    if (ids.isEmpty())
      db.jdbc()
          .update(
              "INSERT INTO \"AspNetRoles\""
                  + " (\"Id\",\"Name\",\"NormalizedName\",\"ConcurrencyStamp\") VALUES (?,?,?,?)",
              roleId,
              role,
              role.toUpperCase(Locale.ROOT),
              UUID.randomUUID().toString());
    db.jdbc().update("DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\"=?", id);
    db.jdbc()
        .update("INSERT INTO \"AspNetUserRoles\" (\"UserId\",\"RoleId\") VALUES (?,?)", id, roleId);
  }

  public void audit(
      String actor, String action, String entity, String ref, String result, String detail) {
    var event = new AuditEvent();
    event.actor = actor;
    event.action = action;
    event.entityType = entity;
    event.entityReference = ref;
    event.result = result;
    event.detail = detail;
    event.correlationId = UUID.randomUUID().toString();
    db.insert(event);
  }

  public void notify(String userId, String relatedId, String subject, String body) {
    queueNotification(userId, relatedId, subject, body, "AccessDecision");
  }

  private void queueNotification(
      String userId, String relatedId, String subject, String body, String type) {
    var user = db.find(ApplicationUser.class, userId).orElseThrow();
    var notification = new UserAccessNotification();
    notification.userId = userId;
    notification.relatedUserId = relatedId;
    notification.email = user.email;
    notification.notificationType = type;
    notification.subject = subject;
    notification.body = body;
    notification.deliveryStatus = "Pending";
    db.insert(notification);
  }
}
