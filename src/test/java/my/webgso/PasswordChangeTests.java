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
class PasswordChangeTests {
  @Autowired PasswordChange changes;
  @Autowired Accounts accounts;
  @Autowired Store db;
  @Autowired MockMvc mvc;
  ApplicationUser user;
  static final String OLD = "OriginalPass123!";
  static final String NEW = "DifferentPass456!";

  @BeforeEach
  void setup() {
    user = accounts.register("password.change", "password.change@gmail.com", "Password User", OLD);
    user.accessStatus = "Approved";
    user.emailConfirmed = true;
    db.update(user);
  }

  String send() {
    changes.request(user.id, OLD);
    var note =
        db.list(
                UserAccessNotification.class,
                "WHERE \"UserId\"=? AND \"NotificationType\"='PasswordChangeCode' ORDER BY \"Id\""
                    + " DESC",
                user.id)
            .getFirst();
    assertThat(note.email).isEqualTo(user.email);
    var matcher = Pattern.compile("code is: ([0-9]{6})").matcher(note.body);
    assertThat(matcher.find()).isTrue();
    return matcher.group(1);
  }

  @Test
  void requiresCurrentPasswordThenCodeBeforeNewPassword() {
    assertThat(changes.stage(user.id)).isEqualTo("request");
    assertThatThrownBy(() -> changes.request(user.id, "wrong"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(changes.stage(user.id)).isEqualTo("request");
    String code = send();
    assertThat(changes.stage(user.id)).isEqualTo("verify");
    assertThatThrownBy(() -> changes.change(user.id, NEW, NEW))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().passwordHash)
        .isEqualTo(user.passwordHash);
    assertThat(changes.verify(user.id, code)).isTrue();
    assertThat(changes.stage(user.id)).isEqualTo("change");
    assertThat(changes.verify(user.id, code)).isFalse();
    changes.change(user.id, NEW, NEW);
    var saved = db.find(ApplicationUser.class, user.id).orElseThrow();
    var encoder = new IdentityPasswordEncoder();
    assertThat(encoder.matches(NEW, saved.passwordHash)).isTrue();
    assertThat(encoder.matches(OLD, saved.passwordHash)).isFalse();
    assertThat(saved.securityStamp).isNotEqualTo(user.securityStamp);
    assertThat(saved.email).isEqualTo(user.email);
    assertThat(changes.stage(user.id)).isEqualTo("request");
    assertThatThrownBy(() -> changes.change(user.id, "ThirdPassword789!", "ThirdPassword789!"))
        .isInstanceOf(IllegalArgumentException.class);
  }

  @Test
  void limitsAttemptsAndResendFrequency() {
    String code = send();
    assertThatThrownBy(() -> changes.request(user.id, OLD))
        .isInstanceOf(IllegalArgumentException.class)
        .hasMessageContaining("60 seconds");
    String wrong = code.equals("000000") ? "111111" : "000000";
    for (int i = 0; i < 5; i++) assertThat(changes.verify(user.id, wrong)).isFalse();
    assertThat(changes.verify(user.id, code)).isFalse();
    assertThat(changes.stage(user.id)).isEqualTo("request");
  }

  @Test
  void expiryAppliesToCodeAndVerifiedPermission() {
    String code = send();
    assertThat(changes.verify(user.id, code)).isTrue();
    db.jdbc()
        .update(
            "UPDATE \"GsoPasswordChanges\" SET \"ExpiresAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now(ZoneOffset.UTC).minusSeconds(1),
            user.id);
    assertThat(changes.stage(user.id)).isEqualTo("request");
    assertThat(changes.verify(user.id, code)).isFalse();
    assertThatThrownBy(() -> changes.change(user.id, NEW, NEW))
        .isInstanceOf(IllegalArgumentException.class);
  }

  @Test
  void boundToUserEmailAndSecurityStamp() {
    String code = send();
    var other = accounts.register("password.other", "password.other@gmail.com", "Other", OLD);
    other.accessStatus = "Approved";
    db.update(other);
    assertThat(changes.verify(other.id, code)).isFalse();
    db.jdbc()
        .update("UPDATE \"AspNetUsers\" SET \"Email\"='changed@gmail.com' WHERE \"Id\"=?", user.id);
    assertThat(changes.verify(user.id, code)).isFalse();
    db.jdbc()
        .update(
            "UPDATE \"AspNetUsers\" SET \"Email\"=?,\"SecurityStamp\"='changed' WHERE \"Id\"=?",
            user.email,
            user.id);
    assertThat(changes.verify(user.id, code)).isFalse();
  }

  @Test
  void validatesNewPasswordAndConfirmationWithoutConsumingVerifiedPermission() {
    assertThat(changes.verify(user.id, send())).isTrue();
    assertThatThrownBy(() -> changes.change(user.id, NEW, "mismatch"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> changes.change(user.id, "short", "short"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> changes.change(user.id, OLD, OLD))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(changes.stage(user.id)).isEqualTo("change");
  }

  @Test
  void mvcEnforcesStagesCsrfAndHidesCodeFromNotifications() throws Exception {
    var auth = accounts.token(user);
    mvc.perform(
            post("/account/profile/password/send-code")
                .with(authentication(auth))
                .param("currentPassword", OLD))
        .andExpect(status().isForbidden());
    mvc.perform(get("/account/profile/password").with(authentication(auth)))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("name=\"confirmPassword\""))));
    String code = send();
    mvc.perform(get("/account/profile/password").with(authentication(auth)))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Verify code")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString(
                            "Your password change verification code is:"))));
    mvc.perform(
            post("/account/profile/password/verify")
                .with(authentication(auth))
                .with(csrf())
                .param("code", code))
        .andExpect(redirectedUrl("/account/profile/password"));
    mvc.perform(get("/account/profile/password").with(authentication(auth)))
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("name=\"confirmPassword\"")));
    mvc.perform(
            post("/account/profile/password/change")
                .with(authentication(auth))
                .with(csrf())
                .param("password", NEW)
                .param("confirmPassword", NEW))
        .andExpect(redirectedUrl("/login?passwordReset=true"));
  }
}
