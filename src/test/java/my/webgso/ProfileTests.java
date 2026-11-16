package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import my.webgso.Models.*;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.Authentication;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class ProfileTests {
  @Autowired MockMvc mvc;
  @Autowired Store db;
  @Autowired Accounts accounts;
  ApplicationUser own, other;
  Authentication auth;

  @BeforeEach
  void setup() {
    own =
        accounts.register(
            "profile.admin", "profile@gmail.com", "Own Profile", "StrongPassword123!");
    own.accessStatus = "Approved";
    db.update(own);
    accounts.setRole(own.id, Accounts.ADMIN);
    auth = accounts.token(own);
    other =
        accounts.register(
            "profile.other",
            "other-profile@gmail.com",
            "Other Private Profile",
            "StrongPassword123!");
  }

  @Test
  void pageDisplaysOnlyOwnProfileAndNewMenu() throws Exception {
    mvc.perform(get("/account/profile").with(authentication(auth)).param("id", other.id))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("My Profile")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString(own.userName)))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("value=\"" + other.email + "\""))))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString(own.passwordHash))));
  }

  @Test
  void editsOwnDetailsIgnoresForgedIdentityAndKeepsAccessControls() throws Exception {
    mvc.perform(
            post("/account/profile")
                .with(authentication(auth))
                .with(csrf())
                .param("displayName", "Updated Profile")
                .param("username", "profile.updated")
                .param("email", own.email)
                .param("phone", "+60 12-3456789")
                .param("id", other.id)
                .param("accessStatus", "Rejected")
                .param("role", "Staff"))
        .andExpect(redirectedUrl("/account/profile"))
        .andExpect(flash().attribute("message", "Your profile has been updated."));
    var saved = db.find(ApplicationUser.class, own.id).orElseThrow();
    assertThat(saved.userName).isEqualTo(own.userName);
    assertThat(saved.normalizedEmail).isEqualTo(own.normalizedEmail);
    assertThat(saved.displayName).isEqualTo("Updated Profile");
    assertThat(saved.phoneNumber).isEqualTo("+60 12-3456789");
    assertThat(saved.passwordHash).isEqualTo(own.passwordHash);
    assertThat(saved.securityStamp).isEqualTo(own.securityStamp);
    assertThat(saved.accessStatus).isEqualTo("Approved");
    assertThat(accounts.roles(own.id)).containsExactly(Accounts.ADMIN);
    assertThat(db.find(ApplicationUser.class, other.id).orElseThrow().email).isEqualTo(other.email);
    assertThat(
            ((Accounts.Principal)
                    accounts
                        .authenticate(
                            UsernamePasswordAuthenticationToken.unauthenticated(
                                own.email, "StrongPassword123!"))
                        .getPrincipal())
                .id())
        .isEqualTo(own.id);
  }

  @Test
  void rejectsDuplicateAndInvalidDetailsWithoutChangingProfile() throws Exception {
    for (String email : new String[] {other.email, "bad-email"}) {
      mvc.perform(
              post("/account/profile")
                  .with(authentication(auth))
                  .with(csrf())
                  .param("displayName", "New name")
                  .param("username", own.userName)
                  .param("email", email))
          .andExpect(redirectedUrl("/account/profile"))
          .andExpect(flash().attributeExists("profileError"));
      assertThat(db.find(ApplicationUser.class, own.id).orElseThrow().email).isEqualTo(own.email);
    }
  }

  @Test
  void profileRequiresSignInAndCsrf() throws Exception {
    mvc.perform(get("/account/profile")).andExpect(status().is3xxRedirection());
    mvc.perform(
            post("/account/profile")
                .with(authentication(auth))
                .param("displayName", "New name")
                .param("username", own.userName)
                .param("email", own.email))
        .andExpect(status().isForbidden());
  }
}

