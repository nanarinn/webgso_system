package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import my.webgso.Models.ApplicationUser;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.Authentication;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest(properties = "spring.datasource.url=jdbc:h2:mem:profile-email;MODE=PostgreSQL;DB_CLOSE_DELAY=-1;DATABASE_TO_UPPER=false")
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class ProfileEmailChangeTests {
  @Autowired Store db;
  @Autowired Accounts accounts;
  @Autowired ProfileService profiles;
  @Autowired MockMvc mvc;
  ApplicationUser user, admin;
  Authentication auth;
  static final String PASSWORD = "StrongPassword123!";

  @BeforeEach void setup() {
    user = create("profile.user@sae-malaysia.com", Accounts.USER);
    admin = create("profile.admin@sae-malaysia.com", Accounts.ADMIN);
    auth = accounts.token(user);
  }

  ApplicationUser create(String email, String role) {
    var u = accounts.register(email, email, role + " Person", PASSWORD);
    u.accessStatus = "Approved";
    db.update(u);
    accounts.setRole(u.id, role);
    return u;
  }

  @Test void userCanEditOwnNameAndOpenSecurityPopups() throws Exception {
    mvc.perform(post("/account/profile").with(authentication(auth)).with(csrf())
        .param("displayName", "Updated User").param("email", user.email)
        .param("userId", admin.id).param("username", admin.email))
        .andExpect(redirectedUrl("/account/profile"));
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().displayName).isEqualTo("Updated User");
    assertThat(db.find(ApplicationUser.class, admin.id).orElseThrow().displayName).isEqualTo("Admin Person");
    for (String type : new String[]{"email", "password"}) {
      String html = mvc.perform(get("/account/profile/" + type).with(authentication(auth)))
          .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
      assertThat(html).contains("Current password", "aria-modal=\"true\"").doesNotContain("Send code", "Verify email");
    }
  }

  @Test void changingEmailUpdatesLoginAndCreatesAdminHistoryWithoutSendingEmail() throws Exception {
    mvc.perform(post("/account/profile/email/change").with(authentication(auth)).with(csrf())
        .param("newEmail", "Updated.User@sae-malaysia.com").param("currentPassword", PASSWORD)
        .param("userId", admin.id)).andExpect(redirectedUrl("/login?emailChanged=true"));
    var changed = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(changed.email).isEqualTo("updated.user@sae-malaysia.com");
    assertThat(changed.userName).isEqualTo(changed.email);
    assertThat(changed.securityStamp).isNotEqualTo(user.securityStamp);
    assertThat(changed.emailConfirmed).isFalse();
    assertThat(accounts.roles(user.id)).containsExactly(Accounts.USER);
    assertThat(db.find(ApplicationUser.class, admin.id).orElseThrow().email).isEqualTo(admin.email);
    assertThat(profiles.emailHistory(user.id)).singleElement().satisfies(h -> {
      assertThat(h.oldEmail()).isEqualTo(user.email);
      assertThat(h.newEmail()).isEqualTo(changed.email);
      assertThat(h.changedAt()).isNotNull();
    });
    assertThat(accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(changed.email, PASSWORD))).isNotNull();
    assertThatThrownBy(() -> accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(user.email, PASSWORD)))
        .isInstanceOf(org.springframework.security.core.AuthenticationException.class);
    mvc.perform(get("/account/profile").with(authentication(auth))).andExpect(status().is3xxRedirection());
    String html = mvc.perform(get("/admin/registered-users").with(authentication(accounts.token(admin))))
        .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
    assertThat(html).contains("Email changed", user.email, changed.email, "Previous email")
        .doesNotContain(PASSWORD, changed.passwordHash);
    mvc.perform(get("/admin/registered-users").with(authentication(accounts.token(changed))))
        .andExpect(status().isForbidden());
    assertThat(db.jdbc().queryForObject("SELECT COUNT(*) FROM \"UserAccessNotifications\" WHERE \"DeliveryStatus\"='Pending' AND \"NotificationType\"='EmailChangeCode'", Long.class)).isZero();
  }

  @Test void invalidEmailDuplicateAndWrongPasswordLeaveAccountAndHistoryUnchanged() {
    for (String email : new String[]{"other@gmail.com", admin.email, user.email, "x@sae-malaysia.com.evil"}) {
      assertThatThrownBy(() -> profiles.changeEmail(user.id, email, PASSWORD)).isInstanceOf(IllegalArgumentException.class);
    }
    assertThatThrownBy(() -> profiles.changeEmail(user.id, "new@sae-malaysia.com", "WrongPassword123!"))
        .isInstanceOf(org.springframework.security.core.AuthenticationException.class);
    var unchanged = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(unchanged.email).isEqualTo(user.email);
    assertThat(unchanged.accessFailedCount).isEqualTo(1);
    assertThat(profiles.emailHistory(user.id)).isEmpty();
  }

  @Test void emailChangeRequiresAuthenticationAndCsrf() throws Exception {
    mvc.perform(post("/account/profile/email/change").with(authentication(auth))
        .param("newEmail", "new@sae-malaysia.com").param("currentPassword", PASSWORD)).andExpect(status().isForbidden());
    mvc.perform(post("/account/profile/email/change").with(csrf())
        .param("newEmail", "new@sae-malaysia.com").param("currentPassword", PASSWORD)).andExpect(status().is3xxRedirection());
    assertThat(profiles.emailHistory(user.id)).isEmpty();
  }

  @Test void userChangesOnlyOwnPasswordWithoutEmailAndOldSessionExpires() throws Exception {
    String next = "ChangedPassword456!";
    mvc.perform(post("/account/profile/password/update").with(authentication(auth)).with(csrf())
        .param("currentPassword", PASSWORD).param("password", next).param("confirmPassword", next)
        .param("userId", admin.id)).andExpect(redirectedUrl("/login?passwordReset=true"));
    assertThat(accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(user.email, next))).isNotNull();
    assertThat(accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(admin.email, PASSWORD))).isNotNull();
    mvc.perform(get("/account/profile").with(authentication(auth))).andExpect(status().is3xxRedirection());
    assertThatThrownBy(() -> accounts.authenticate(UsernamePasswordAuthenticationToken.unauthenticated(user.email, PASSWORD)))
        .isInstanceOf(org.springframework.security.core.AuthenticationException.class);
  }
}
