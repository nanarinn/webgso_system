package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.time.*;
import java.util.*;
import java.util.regex.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class AccountToolsTests {
  @Autowired Store db;
  @Autowired Accounts accounts;
  @Autowired PasswordReset resets;
  @Autowired EmailPreferences prefs;
  @Autowired ApiEmailAlerts alerts;
  @Autowired MockMvc mvc;
  ApplicationUser user;

  @BeforeEach
  void setup() {
    user =
        accounts.register("tools.user", "tools.user@gmail.com", "Tools User", "OriginalPass123!");
    user.accessStatus = "Approved";
    user.emailConfirmed = true;
    db.update(user);
  }

  String requestToken() {
    resets.request(user.email);
    var notes =
        db.list(
            UserAccessNotification.class,
            "WHERE \"UserId\"=? AND \"NotificationType\"='PasswordReset' ORDER BY \"Id\" DESC",
            user.id);
    var matcher = Pattern.compile("token=([A-Za-z0-9_-]{43})").matcher(notes.getFirst().body);
    assertThat(matcher.find()).isTrue();
    return matcher.group(1);
  }

  @Test
  void pendingOptionalMailIsCancelledAfterOptOutOrRecipientChange() {
    var note = new UserAccessNotification();
    note.userId = user.id;
    note.email = user.email;
    note.notificationType = "ApiTestFailed";
    assertThat(prefs.get(user.id).failure()).isFalse();
    prefs.save(user.id, new EmailPreferences.Settings(true, true, true, true, true));
    assertThat(prefs.allowsDelivery(note)).isTrue();
    prefs.save(user.id, new EmailPreferences.Settings(false, true, true, true, true));
    assertThat(prefs.allowsDelivery(note)).isFalse();
    prefs.save(user.id, new EmailPreferences.Settings(true, true, true, true, true));
    note.email = "previous@gmail.com";
    assertThat(prefs.allowsDelivery(note)).isFalse();
    note.notificationType = "PasswordReset";
    assertThat(prefs.allowsDelivery(note)).isTrue();
  }

  @Test
  void resetPostRequiresCsrfAndMatchingPasswordsWithoutConsumingLink() throws Exception {
    String token = requestToken();
    mvc.perform(
            post("/account/reset-password")
                .param("token", token)
                .param("password", "Replacement123!Password")
                .param("confirmPassword", "Replacement123!Password"))
        .andExpect(status().isForbidden());
    mvc.perform(
            post("/account/reset-password")
                .with(csrf())
                .param("token", token)
                .param("password", "Replacement123!Password")
                .param("confirmPassword", "DifferentPassword123!"))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("Passwords do not match")));
    assertThat(resets.reset(token, "Replacement123!Password")).isTrue();
  }

  @Test
  void singleUseResetChangesHashAndStampAndSignsOutExistingSession() throws Exception {
    var oldAuth = accounts.token(user);
    String stamp = user.securityStamp;
    String token = requestToken();
    String hash =
        db.jdbc()
            .queryForObject(
                "SELECT \"TokenHash\" FROM \"GsoPasswordResets\" WHERE \"UserId\"=?",
                String.class,
                user.id);
    assertThat(hash).hasSize(64).isNotEqualTo(token);
    mvc.perform(get("/account/reset-password").param("token", token))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Choose a new password")));
    assertThat(resets.reset(token, "Replacement123!Password")).isTrue();
    assertThat(resets.reset(token, "AnotherPassword123!")).isFalse();
    var fresh = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(fresh.securityStamp).isNotEqualTo(stamp);
    assertThat(new IdentityPasswordEncoder().matches("Replacement123!Password", fresh.passwordHash))
        .isTrue();
    mvc.perform(get("/account/profile").with(authentication(oldAuth)))
        .andExpect(redirectedUrl("/login?reason=session-timeout"));
  }

  @Test
  void expiredChangedEmailAndSuspendedAccountCannotReset() {
    String token = requestToken();
    db.jdbc()
        .update(
            "UPDATE \"GsoPasswordResets\" SET \"ExpiresAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC).minusMinutes(1),
            user.id);
    assertThat(resets.reset(token, "Replacement123!Password")).isFalse();
    db.jdbc()
        .update(
            "UPDATE \"GsoPasswordResets\" SET \"ExpiresAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC).plusMinutes(10),
            user.id);
    user.email = "changed@gmail.com";
    user.normalizedEmail = "CHANGED@GMAIL.COM";
    db.update(user);
    assertThat(resets.reset(token, "Replacement123!Password")).isFalse();
    user.email = "tools.user@gmail.com";
    user.normalizedEmail = "TOOLS.USER@GMAIL.COM";
    user.accessStatus = "Inactive";
    db.update(user);
    assertThat(resets.reset(token, "Replacement123!Password")).isFalse();
  }

  @Test
  void resendIsLimitedAndInvalidRequestsAreGenericAndCsrfProtected() throws Exception {
    String token = requestToken();
    resets.request(user.email);
    assertThat(
            db.jdbc()
                .queryForObject(
                    "SELECT COUNT(*) FROM \"GsoPasswordResets\" WHERE \"UserId\"=?",
                    Long.class,
                    user.id))
        .isEqualTo(1);
    mvc.perform(post("/account/forgot-password").param("email", user.email))
        .andExpect(status().isForbidden());
    for (String email : List.of(user.email, "unknown@gmail.com", "notgmail@example.com"))
      mvc.perform(post("/account/forgot-password").param("email", email).with(csrf()))
          .andExpect(status().isOk())
          .andExpect(content().string(org.hamcrest.Matchers.containsString("Check your account ID")));
    assertThatThrownBy(() -> resets.reset(token, "short"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(resets.reset(token, "Replacement123!Password")).isTrue();
  }

  @Test
  void currentPasswordChangeNeedsProofAndConfirmationAndSendsNoEmail() throws Exception {
    String stamp = user.securityStamp;
    mvc.perform(post("/account/forgot-password").with(csrf())
        .param("identifier", user.email).param("currentPassword", "WrongPassword123!")
        .param("password", "Replacement123!Password").param("confirmPassword", "Replacement123!Password"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Check your account ID")));
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().securityStamp).isEqualTo(stamp);
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().accessFailedCount).isEqualTo(1);
    mvc.perform(post("/account/forgot-password").with(csrf())
        .param("identifier", user.email).param("currentPassword", "OriginalPass123!")
        .param("password", "Replacement123!Password").param("confirmPassword", "DifferentPassword123!"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Passwords do not match")));
    mvc.perform(post("/account/forgot-password").with(csrf())
        .param("identifier", user.email).param("currentPassword", "OriginalPass123!")
        .param("password", "Replacement123!Password").param("confirmPassword", "Replacement123!Password"))
        .andExpect(redirectedUrl("/login?passwordReset=true"));
    var updated = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(new IdentityPasswordEncoder().matches("Replacement123!Password", updated.passwordHash)).isTrue();
    assertThat(updated.securityStamp).isNotEqualTo(stamp);
    assertThat(db.list(UserAccessNotification.class,
        "WHERE \"UserId\"=? AND \"NotificationType\" IN ('PasswordReset','PasswordChangeCode')", user.id)).isEmpty();
  }

  @Test
  void usernameAccountCanViewEditProfileAndChangePasswordWithoutEmail() throws Exception {
    var local = accounts.registerUsername("no.mail.user", "OriginalPass123!");
    var auth = accounts.token(local);
    mvc.perform(get("/account/profile").with(authentication(auth))).andExpect(status().isOk());
    mvc.perform(post("/account/profile").with(authentication(auth)).with(csrf())
        .param("displayName", "Local User").param("email", "").param("phone", ""))
        .andExpect(status().is3xxRedirection());
    accounts.changeWithCurrentPassword(local.userName, "OriginalPass123!", "NewPassword123!", "NewPassword123!");
    assertThat(db.list(UserAccessNotification.class, "WHERE \"UserId\"=?", local.id)).isEmpty();
  }

  @Test
  void onlyOwnPreferencesAreEditableAndSecurityEmailsCannotBeDisabled() throws Exception {
    mvc.perform(get("/account/email-settings")).andExpect(status().is3xxRedirection());
    mvc.perform(
            post("/account/email-settings")
                .with(authentication(accounts.token(user)))
                .with(csrf())
                .param("failure", "true")
                .param("userId", "another-user"))
        .andExpect(redirectedUrl("/account/email-settings"));
    assertThat(prefs.get(user.id))
        .isEqualTo(new EmailPreferences.Settings(true, false, false, false, false));
    assertThat(prefs.allows(user.id, "PasswordReset")).isTrue();
    assertThat(prefs.allows(user.id, "EmailVerification")).isTrue();
    mvc.perform(get("/account/email-settings").with(authentication(accounts.token(user))))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Database backup")));
    mvc.perform(get("/help").with(authentication(accounts.token(user))))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Understand the result")));
  }

  @Test
  void optedInFailureRecoveryAndStaleAlertsFollowPreferences() {
    var alerts =
        new ApiEmailAlerts(
            db,
            mock(MailDelivery.class),
            new org.springframework.mock.env.MockEnvironment()
                .withProperty("webgso.api-alerts.enabled", "true"));
    alerts.setPreferences(prefs);
    var plant = new LssPlant();
    plant.name = "Test plant";
    db.insert(plant);
    prefs.save(user.id, new EmailPreferences.Settings(false, false, false, false, false));
    long before = db.count(UserAccessNotification.class);
    alerts.record(plant.id, "MMF", null, false, 503);
    alerts.recordStale(plant.id, "MMF", null, "Success. Warning: Stale data");
    assertThat(db.count(UserAccessNotification.class)).isEqualTo(before);
    prefs.save(user.id, new EmailPreferences.Settings(true, true, true, false, false));
    alerts.record(plant.id, "MMF", null, false, 503);
    alerts.record(plant.id, "MMF", "Failed", true, 200);
    alerts.recordStale(plant.id, "MMF", null, "Success. Warning: Stale data");
    assertThat(db.list(UserAccessNotification.class, "WHERE \"UserId\"=?", user.id))
        .extracting(n -> n.notificationType)
        .contains("ApiTestFailed", "ApiTestRecovered", "StaleDataAlert");
    before = db.count(UserAccessNotification.class);
    alerts.recordStale(plant.id, "MMF", "Warning: Stale data", "Success. Warning: Stale data");
    // Actual stored previous messages include the same warning delimiter.
    alerts.recordStale(
        plant.id, "MMF", "Success. Warning: Stale data", "Success. Warning: Stale data");
    assertThat(db.count(UserAccessNotification.class)).isEqualTo(before);
  }
}
