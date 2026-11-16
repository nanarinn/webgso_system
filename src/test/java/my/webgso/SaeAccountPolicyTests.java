package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.csrf;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.util.List;
import my.webgso.Models.ApplicationUser;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest(properties = {
    "webgso.accounts.sae-only=true",
    "spring.datasource.url=jdbc:h2:mem:webgso-sae-policy;MODE=PostgreSQL;DB_CLOSE_DELAY=-1;DATABASE_TO_UPPER=false"
})
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class SaeAccountPolicyTests {
  @Autowired Accounts accounts;
  @Autowired Store db;
  @Autowired MockMvc mvc;
  @Autowired EmailVerification verification;
  @Autowired UserManagement management;

  @Test
  void adminCannotActivateAnUnverifiedSignup() {
    var admin = accounts.register("verification.admin", "admin@sae-malaysia.com", "Admin", "StrongPassword123!");
    admin.accessStatus = "Approved";
    db.update(admin);
    accounts.setRole(admin.id, Accounts.ADMIN);
    var user = verification.registerSaeEmail("suaid.ishak@sae-malaysia.com", "StrongPassword123!");
    assertThatThrownBy(() -> management.manage(accounts.token(admin), user.id, "activate", ""))
        .isInstanceOf(IllegalArgumentException.class).hasMessageContaining("verify their email");
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().accessStatus).isEqualTo("Pending");
  }

  private String token(String id) {
    var note = db.list(my.webgso.Models.UserAccessNotification.class,
        "WHERE \"UserId\"=? AND \"NotificationType\"='EmailVerification'", id).getFirst();
    assertThat(note.email).endsWith("@sae-malaysia.com");
    var match = java.util.regex.Pattern.compile("token=([A-Za-z0-9_-]{43})").matcher(note.body);
    assertThat(match.find()).isTrue();
    return match.group(1);
  }

  @Test
  void rejectsOtherDomainsAndBypassAttemptsOnServer() throws Exception {
    for (String address : List.of("user@gmail.com", "user@sae-malaysia.com.evil.test",
        "user@sub.sae-malaysia.com", "user@@sae-malaysia.com", "plain.username",
        ".user@sae-malaysia.com", "user..name@sae-malaysia.com")) {
      assertThat(SaeAccounts.allowed(address)).isFalse();
      mvc.perform(post("/account/register").with(csrf()).param("email", address)
          .param("password", "StrongPassword123!"))
          .andExpect(status().isBadRequest());
    }
    assertThat(SaeAccounts.allowed("Developer@SAE-MALAYSIA.COM")).isTrue();
    assertThat(SaeAccounts.allowed(null)).isFalse();
  }

  @Test
  void companyEmailSignupThenLoginAndDuplicateProtection() throws Exception {
    mvc.perform(post("/account/register").with(csrf())
        .param("email", "Developer@SAE-MALAYSIA.COM").param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?registered=success"));
    var user = db.list(ApplicationUser.class, "WHERE \"NormalizedEmail\"=?", "DEVELOPER@SAE-MALAYSIA.COM").getFirst();
    assertThat(user.email).isEqualTo("developer@sae-malaysia.com");
    assertThat(user.emailConfirmed).isFalse();
    assertThat(user.accessStatus).isEqualTo("Approved");
    assertThat(accounts.roles(user.id)).containsExactly(Accounts.USER);
    assertThat(db.list(my.webgso.Models.UserAccessNotification.class,
        "WHERE \"UserId\"=? AND \"NotificationType\"='EmailVerification'", user.id)).isEmpty();
    mvc.perform(post("/account/login").with(csrf())
        .param("identifier", "DEVELOPER@SAE-MALAYSIA.COM").param("password", "CHANGEME"))
        .andExpect(redirectedUrl("/account/first-password"));
    mvc.perform(post("/account/register").with(csrf())
        .param("email", "developer@sae-malaysia.com").param("password", "StrongPassword123!"))
        .andExpect(status().isBadRequest());
    mvc.perform(post("/account/register").param("email", "second@sae-malaysia.com")
        .param("password", "StrongPassword123!"))
        .andExpect(status().isForbidden());
  }

  @Test
  void suaidCanRegisterAndLoginAsUser() throws Exception {
    mvc.perform(post("/account/register").with(csrf())
        .param("email", "Suaid.Ishak@SAE-MALAYSIA.COM").param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?registered=success"));
    var user = db.list(ApplicationUser.class, "WHERE \"NormalizedEmail\"=?", "SUAID.ISHAK@SAE-MALAYSIA.COM").getFirst();
    assertThat(accounts.roles(user.id)).containsExactly(Accounts.USER);
    mvc.perform(post("/account/login").with(csrf())
        .param("identifier", user.email).param("password", "CHANGEME"))
        .andExpect(redirectedUrl("/account/first-password"));
  }

  @Test
  void anyCompanyMailboxCanRegisterAndLoginWithoutVerification() throws Exception {
    for (String email : List.of("unknown@sae-malaysia.com", "developer+other@sae-malaysia.com",
        "developer2@sae-malaysia.com")) {
      mvc.perform(post("/account/register").with(csrf()).param("email", email)
          .param("password", "StrongPassword123!"))
          .andExpect(redirectedUrl("/login?registered=success"));
      var user = db.list(ApplicationUser.class, "WHERE \"NormalizedEmail\"=?",
          email.toUpperCase(java.util.Locale.ROOT)).getFirst();
      assertThat(user.accessStatus).isEqualTo("Approved");
      assertThat(user.emailConfirmed).isFalse();
      assertThat(accounts.roles(user.id)).containsExactly(Accounts.USER);
      mvc.perform(post("/account/login").with(csrf()).param("identifier", email)
          .param("password", "CHANGEME"))
          .andExpect(redirectedUrl("/account/first-password"));
    }
  }

  @Test
  void existingGmailAndUsernameAccountsCannotBypassCompanyRestriction() {
    var user = accounts.register("legacy.user", "legacy.user@gmail.com", "Legacy", "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    for (String login : List.of(user.email, user.userName)) {
      assertThatThrownBy(() -> accounts.authenticate(
          UsernamePasswordAuthenticationToken.unauthenticated(login, "StrongPassword123!")))
          .isInstanceOf(BadCredentialsException.class);
    }
    var company = accounts.registerSaeEmail("developer@sae-malaysia.com", "StrongPassword123!");
    assertThatThrownBy(() -> accounts.authenticate(
        UsernamePasswordAuthenticationToken.unauthenticated(company.email, "WrongPassword123!")))
        .isInstanceOf(BadCredentialsException.class);
    assertThat(db.find(ApplicationUser.class, company.id).orElseThrow().accessFailedCount).isEqualTo(1);
  }
}

