package my.webgso;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

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
  void googleLoginIsUnavailableEvenWithOldConfiguration() throws Exception {
    mvc.perform(get("/account/google")).andExpect(redirectedUrl("/login"));
    mvc.perform(get("/oauth2/authorization/google")).andExpect(redirectedUrl("/login"));
    mvc.perform(get("/login/oauth2/code/google").param("code", "old").param("state", "old"))
        .andExpect(redirectedUrl("/login"));
  }

  @Test
  void loginAndSignupOfferOnlyEmailAuthentication() throws Exception {
    for (String path : new String[] {"/login", "/register"}) {
      mvc.perform(get(path))
          .andExpect(status().isOk())
          .andExpect(
              content()
                  .string(
                      org.hamcrest.Matchers.not(
                          org.hamcrest.Matchers.containsString("Continue with Google"))));
    }
  }
}
