package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.net.URI;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest(
    properties = {
      "webgso.google.enabled=true",
      "webgso.google.client-id=test.apps.googleusercontent.com",
      "webgso.google.client-secret=test-only"
    })
@AutoConfigureMockMvc
@ActiveProfiles("test")
class GoogleOAuthSecurityTests {
  @Autowired MockMvc mvc;

  @Test
  void googleAuthorizationUsesStateNonceAndFixedCallback() throws Exception {
    mvc.perform(get("/account/google")).andExpect(redirectedUrl("/oauth2/authorization/google"));
    var result =
        mvc.perform(get("/oauth2/authorization/google"))
            .andExpect(status().is3xxRedirection())
            .andReturn();
    var target = URI.create(result.getResponse().getRedirectedUrl());
    assertThat(target.getHost()).isEqualTo("accounts.google.com");
    assertThat(target.getQuery())
        .contains(
            "state=",
            "nonce=",
            "response_type=code",
            "openid",
            "email",
            "profile",
            "redirect_uri=http://localhost:5187/login/oauth2/code/google");
    assertThat(result.getRequest().getSession(false)).isNotNull();
  }

  @Test
  void unsolicitedCallbackDoesNotCreateAnAuthenticatedSession() throws Exception {
    var result =
        mvc.perform(
                get("/login/oauth2/code/google")
                    .param("code", "unsolicited")
                    .param("state", "invalid"))
            .andExpect(redirectedUrl("/login?google=error"))
            .andReturn();
    assertThat(result.getRequest().getSession(false)).isNull();
    mvc.perform(get("/api/status")).andExpect(status().isUnauthorized());
  }
}
