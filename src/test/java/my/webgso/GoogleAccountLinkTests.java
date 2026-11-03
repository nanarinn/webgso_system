package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.time.Instant;
import my.webgso.Models.ApplicationUser;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest(
    properties = {
      "webgso.google.enabled=true",
      "webgso.google.client-id=test.apps.googleusercontent.com",
      "webgso.google.client-secret=test-only"
    })
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class GoogleAccountLinkTests {
  @Autowired Store db;
  @Autowired Accounts accounts;
  @Autowired GoogleAccountLink links;
  @Autowired MockMvc mvc;
  ApplicationUser user;
  static final String PASSWORD = "StrongPassword123!";

  @BeforeEach
  void setup() {
    user = accounts.register("link.test", "link.test@gmail.com", "Link Test", PASSWORD);
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, Accounts.ADMIN);
  }

  @Test
  void verifiedIdentityLinksWithoutReplacingLocalAccountOrPassword() {
    var intent = links.begin(accounts.token(user), PASSWORD);
    var hash = db.find(ApplicationUser.class, user.id).orElseThrow().passwordHash;
    links.complete(intent, "link-subject", "LINK.TEST@gmail.com", true);
    assertThat(links.linked(user.id)).isTrue();
    var saved = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(saved.passwordHash).isEqualTo(hash);
    assertThat(saved.email).isEqualTo(user.email);
    assertThat(
            accounts
                .authenticate(
                    org.springframework.security.authentication.UsernamePasswordAuthenticationToken
                        .unauthenticated(user.email, PASSWORD))
                .isAuthenticated())
        .isTrue();
  }

  @Test
  void rejectsWrongEmailUnverifiedIdentityAndExpiredOrChangedSession() {
    var intent = links.begin(accounts.token(user), PASSWORD);
    assertThatThrownBy(() -> links.complete(intent, "subject", "other@gmail.com", true))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> links.complete(intent, "subject", user.email, false))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(
            () ->
                links.current(
                    new GoogleAccountLink.Intent(
                        user.id, user.securityStamp, Instant.now().minusSeconds(1), null)))
        .isInstanceOf(IllegalArgumentException.class);
    user.securityStamp = "changed";
    db.update(user);
    assertThatThrownBy(() -> links.complete(intent, "subject", user.email, true))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(links.linked(user.id)).isFalse();
  }

  @Test
  void cannotStealGoogleIdentityFromAnotherUser() {
    var other = accounts.register("other.link", "other.link@gmail.com", "Other", PASSWORD);
    db.jdbc()
        .update(
            "INSERT INTO \"GsoGoogleIdentities\" (\"Subject\",\"UserId\") VALUES (?,?)",
            "owned-subject",
            other.id);
    var intent = links.begin(accounts.token(user), PASSWORD);
    assertThatThrownBy(() -> links.complete(intent, "owned-subject", user.email, true))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(links.linked(user.id)).isFalse();
    assertThat(links.linked(other.id)).isTrue();
  }

  @Test
  void linkRequiresPasswordCsrfAndBindsIntentToOAuthState() throws Exception {
    var auth = accounts.token(user);
    mvc.perform(post("/account/profile/google/link").with(authentication(auth)))
        .andExpect(status().isForbidden());
    mvc.perform(
            post("/account/profile/google/link")
                .with(authentication(auth))
                .with(csrf())
                .param("currentPassword", "wrong"))
        .andExpect(redirectedUrl("/account/profile"))
        .andExpect(request().sessionAttributeDoesNotExist(GoogleAccountLink.SESSION_KEY));
    var started =
        mvc.perform(
                post("/account/profile/google/link")
                    .with(authentication(auth))
                    .with(csrf())
                    .param("currentPassword", PASSWORD))
            .andExpect(redirectedUrl("/oauth2/authorization/google"))
            .andReturn();
    var session = (org.springframework.mock.web.MockHttpSession) started.getRequest().getSession();
    var authorized =
        mvc.perform(get("/oauth2/authorization/google").session(session))
            .andExpect(status().is3xxRedirection())
            .andReturn();
    assertThat(authorized.getResponse().getRedirectedUrl()).contains("prompt=select_account");
    var intent = (GoogleAccountLink.Intent) session.getAttribute(GoogleAccountLink.SESSION_KEY);
    assertThat(intent.state()).isNotBlank();
    assertThat(intent.userId()).isEqualTo(user.id);
    mvc.perform(get("/account/profile").with(authentication(auth)))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Link Google account")));
  }
}
