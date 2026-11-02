package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.time.*;
import java.util.regex.Pattern;
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
class EmailChangeTests {
  @Autowired EmailChange changes;
  @Autowired ProfileService profiles;
  @Autowired Accounts accounts;
  @Autowired Store db;
  @Autowired MockMvc mvc;
  ApplicationUser user;
  static final String PASS = "StrongPassword123!";

  @BeforeEach
  void setup() {
    user = accounts.register("email.change", "email.change@gmail.com", "Email Change", PASS);
    user.accessStatus = "Approved";
    user.emailConfirmed = true;
    db.update(user);
  }

  String send() {
    changes.request(user.id, "new.email@gmail.com", PASS);
    var note =
        db.list(
                UserAccessNotification.class,
                "WHERE \"UserId\"=? AND \"NotificationType\"='EmailChangeCode' ORDER BY \"Id\""
                    + " DESC",
                user.id)
            .getFirst();
    assertThat(note.email).isEqualTo("new.email@gmail.com");
    var matcher = Pattern.compile("code is: ([0-9]{6})").matcher(note.body);
    assertThat(matcher.find()).isTrue();
    return matcher.group(1);
  }

  @Test
  void profileShowsSeparateCodeFormsAndVerifiesThroughAuthenticatedPost() throws Exception {
    var auth = accounts.token(user);
    mvc.perform(get("/account/profile/edit").with(authentication(auth)))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Send code")));
    String code = send();
    mvc.perform(get("/account/profile/edit").with(authentication(auth)))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString(
                            "Your email change verification code is:"))))
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("Verify &amp; change email")));
    mvc.perform(
            post("/account/profile/email/verify")
                .with(authentication(auth))
                .with(csrf())
                .param("code", code))
        .andExpect(redirectedUrl("/login?emailChanged=true"));
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().email)
        .isEqualTo("new.email@gmail.com");
  }

  @Test
  void rejectsChangedAvailabilityAndManagedAccounts() {
    String code = send();
    accounts.register("taken.later", "new.email@gmail.com", "Other", PASS);
    assertThatThrownBy(() -> changes.confirm(user.id, code))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().email).isEqualTo(user.email);
    user.isEntraUser = true;
    db.update(user);
    assertThatThrownBy(() -> changes.request(user.id, "another@gmail.com", PASS))
        .isInstanceOf(IllegalArgumentException.class);
  }

  @Test
  void changesOnlyAfterVerificationAndCannotReuseCode() {
    String code = send();
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().email).isEqualTo(user.email);
    assertThat(changes.pending(user.id)).contains("new.email@gmail.com");
    assertThat(changes.confirm(user.id, code)).isTrue();
    var saved = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(saved.email).isEqualTo("new.email@gmail.com");
    assertThat(saved.emailConfirmed).isTrue();
    assertThat(saved.securityStamp).isNotEqualTo(user.securityStamp);
    assertThat(saved.userName).isEqualTo(user.userName);
    assertThat(changes.confirm(user.id, code)).isFalse();
    assertThat(
            db.list(
                    UserAccessNotification.class,
                    "WHERE \"UserId\"=? AND \"NotificationType\"='EmailChangeNotice'",
                    user.id)
                .getFirst()
                .email)
        .isEqualTo(user.email);
  }

  @Test
  void rejectsWrongCodesAndLimitsAttempts() {
    String code = send();
    String wrong = code.equals("000000") ? "111111" : "000000";
    for (int i = 0; i < 5; i++) assertThat(changes.confirm(user.id, wrong)).isFalse();
    assertThat(changes.confirm(user.id, code)).isFalse();
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().email).isEqualTo(user.email);
  }

  @Test
  void expiresAndReplacesOldCodesWithResendThrottle() {
    String code = send();
    assertThatThrownBy(() -> changes.request(user.id, "new.email@gmail.com", PASS))
        .isInstanceOf(IllegalArgumentException.class)
        .hasMessageContaining("60 seconds");
    db.jdbc()
        .update(
            "UPDATE \"GsoEmailChanges\" SET \"ExpiresAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC).minusSeconds(1),
            user.id);
    assertThat(changes.confirm(user.id, code)).isFalse();
    db.jdbc()
        .update(
            "UPDATE \"GsoEmailChanges\" SET \"RequestedAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC).minusMinutes(2),
            user.id);
    changes.request(user.id, "different.email@gmail.com", PASS);
    assertThat(changes.pending(user.id)).contains("different.email@gmail.com");
    assertThat(changes.confirm(user.id, code)).isFalse();
  }

  @Test
  void rejectsBypassBadPasswordNonGmailAndDuplicate() {
    assertThatThrownBy(
            () -> profiles.update(user.id, "Name", user.userName, "new.email@gmail.com", ""))
        .isInstanceOf(IllegalArgumentException.class)
        .hasMessageContaining("Send code");
    assertThatThrownBy(() -> changes.request(user.id, "new.email@gmail.com", "wrong"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> changes.request(user.id, "other@example.com", PASS))
        .isInstanceOf(IllegalArgumentException.class);
    accounts.register("other.email", "taken@gmail.com", "Other", PASS);
    assertThatThrownBy(() -> changes.request(user.id, "taken@gmail.com", PASS))
        .isInstanceOf(IllegalArgumentException.class);
  }

  @Test
  void bindsChallengeToOwnerStampAndRequiresCsrf() throws Exception {
    String code = send();
    var other = accounts.register("email.other", "email.other@gmail.com", "Other", PASS);
    other.accessStatus = "Approved";
    db.update(other);
    assertThat(changes.confirm(other.id, code)).isFalse();
    db.jdbc()
        .update("UPDATE \"AspNetUsers\" SET \"SecurityStamp\"='changed' WHERE \"Id\"=?", user.id);
    assertThat(changes.confirm(user.id, code)).isFalse();
    mvc.perform(
            post("/account/profile/email/verify")
                .with(authentication(accounts.token(other)))
                .param("code", code))
        .andExpect(status().isForbidden());
  }
}
