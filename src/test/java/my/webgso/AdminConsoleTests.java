package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest(properties = "spring.datasource.url=jdbc:h2:mem:admin-console;MODE=PostgreSQL;DB_CLOSE_DELAY=-1;DATABASE_TO_UPPER=false")
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class AdminConsoleTests {
  @Autowired Accounts accounts;
  @Autowired Store db;
  @Autowired MockMvc mvc;
  @Autowired UserManagement management;
  @Autowired ProfileService profiles;

  @Test void auditLogIsAdminOnlyAndMenuFollowsUserManagement() throws Exception {
    var admin = account(Accounts.ADMIN);
    var viewer = account(Accounts.USER);
    String html = mvc.perform(get("/admin/audit-log").with(authentication(admin)))
        .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
    assertThat(html).contains("Audit Log", "Time (MYT)", "Search activity");
    assertThat(html.indexOf("href=\"/admin/registered-users\""))
        .isLessThan(html.indexOf("href=\"/admin/audit-log\""));
    assertThat(html.indexOf("href=\"/admin/audit-log\""))
        .isLessThan(html.indexOf("href=\"/account/profile\""));
    mvc.perform(get("/admin/audit-log").with(authentication(viewer))).andExpect(status().isForbidden());
    mvc.perform(get("/admin/audit-log")).andExpect(status().is3xxRedirection());
    String userHtml = mvc.perform(get("/account/profile").with(authentication(viewer)))
        .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
    assertThat(userHtml).doesNotContain("href=\"/admin/audit-log\"");
  }

  @Test void auditSearchIsLiteralEscapedAndPaginatedNewestFirst() throws Exception {
    var admin = account(Accounts.ADMIN);
    for (int i = 0; i < 51; i++) {
      var event = new my.webgso.Models.AuditEvent();
      event.actor = "audit-search_100%";
      event.action = "Action " + i;
      event.detail = "<script>alert('audit')</script>";
      event.occurredAt = java.time.OffsetDateTime.parse("2026-01-01T00:00:00Z").plusSeconds(i);
      db.insert(event);
    }
    var result = mvc.perform(get("/admin/audit-log").param("q", "audit-search_100%")
        .with(authentication(admin))).andExpect(status().isOk())
        .andExpect(model().attribute("totalEvents", 51L)).andExpect(model().attribute("hasNext", true))
        .andReturn();
    @SuppressWarnings("unchecked")
    var events = (java.util.List<my.webgso.Models.AuditEvent>)result.getModelAndView().getModel().get("events");
    assertThat(events).hasSize(50);
    assertThat(events.getFirst().action).isEqualTo("Action 50");
    assertThat(result.getResponse().getContentAsString()).contains("&lt;script&gt;", "01 Jan 2026, 08:00:50")
        .doesNotContain("<script>alert('audit')</script>");
    mvc.perform(get("/admin/audit-log").param("q", "audit-search_100%")
        .param("page", "999").with(authentication(admin)))
        .andExpect(status().isOk()).andExpect(model().attribute("page", 1))
        .andExpect(model().attribute("hasNext", false));
    mvc.perform(get("/admin/audit-log").param("q", "' OR 1=1 --").with(authentication(admin)))
        .andExpect(status().isOk()).andExpect(model().attribute("totalEvents", 0L));
  }


  private org.springframework.security.core.Authentication account(String role) {
    var user = accounts.register("console." + role.toLowerCase(), "console." + role.toLowerCase() + "@sae-malaysia.com", "Console " + role, "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, role);
    return accounts.token(user);
  }

  @Test void registeredUsersAreAdminOnlyAndDoNotExposeSecrets() throws Exception {
    var admin = account(Accounts.ADMIN);
    var viewer = account(Accounts.USER);
    String html = mvc.perform(get("/admin/registered-users").with(authentication(admin)))
        .andExpect(status().isOk()).andExpect(model().attribute("totalUsers", 2L))
        .andReturn().getResponse().getContentAsString();
    assertThat(html).contains("Console Admin", "Console User", "User Management")
        .doesNotContain("PasswordHash", "SecurityStamp", "/review", "Server controls");
    for (String path : new String[]{"/admin/registered-users", "/admin/server-controls"}) {
      mvc.perform(get(path).with(authentication(viewer))).andExpect(status().isForbidden());
      mvc.perform(get(path)).andExpect(status().is3xxRedirection());
    }
    mvc.perform(post("/admin/server-controls/restart").with(authentication(viewer)).with(csrf())
        .param("confirmation", "RESTART").param("currentPassword", "StrongPassword123!"))
        .andExpect(status().isForbidden());

  }

  @Test void adminManagesOnlyUsersWithCsrfAndConfirmation() throws Exception {
    var admin = account(Accounts.ADMIN);
    var viewer = account(Accounts.USER);
    String id = ((Accounts.Principal)viewer.getPrincipal()).id();
    String base = "/admin/registered-users/" + id + "/";
    String stamp = db.find(my.webgso.Models.ApplicationUser.class, id).orElseThrow().securityStamp;
    mvc.perform(post(base + "deactivate").with(authentication(admin))).andExpect(status().isForbidden());
    mvc.perform(post(base + "deactivate").with(authentication(viewer)).with(csrf())).andExpect(status().isForbidden());
    mvc.perform(post(base + "deactivate").with(authentication(admin)).with(csrf())).andExpect(status().is3xxRedirection());
    var disabled = db.find(my.webgso.Models.ApplicationUser.class, id).orElseThrow();
    assertThat(disabled.accessStatus).isEqualTo("Inactive");
    assertThat(disabled.securityStamp).isNotEqualTo(stamp);
    assertThatThrownBy(() -> accounts.authenticate(org.springframework.security.authentication.UsernamePasswordAuthenticationToken
        .unauthenticated(disabled.email, "StrongPassword123!"))).isInstanceOf(org.springframework.security.core.AuthenticationException.class);
    management.manage(admin, id, "activate", "");
    assertThat(db.find(my.webgso.Models.ApplicationUser.class, id).orElseThrow().accessStatus).isEqualTo("Approved");
    assertThat(accounts.roles(id)).containsExactly(Accounts.USER);
    assertThatThrownBy(() -> management.manage(viewer, id, "delete", "delete"))
        .isInstanceOf(org.springframework.security.access.AccessDeniedException.class);
    String adminId = ((Accounts.Principal)admin.getPrincipal()).id();
    for (String op : new String[]{"activate", "deactivate", "delete"})
      assertThatThrownBy(() -> management.manage(admin, adminId, op, "delete")).isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> management.manage(admin, id, "delete", "")).isInstanceOf(IllegalArgumentException.class);
    management.manage(admin, id, "delete", "delete");
    assertThat(db.find(my.webgso.Models.ApplicationUser.class, id)).isEmpty();
    assertThat(accounts.roles(id)).isEmpty();
  }

  @Test void deletedAccountGetsSpecificLoginMessageButNewRegistrationCanSignIn() throws Exception {
    var admin = account(Accounts.ADMIN);
    var viewer = account(Accounts.USER);
    String id = ((Accounts.Principal)viewer.getPrincipal()).id();
    String email = "console.user@sae-malaysia.com";
    management.manage(admin, id, "delete", "delete");
    mvc.perform(post("/account/login").with(csrf()).param("identifier", "  CONSOLE.USER@SAE-MALAYSIA.COM  ")
        .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?accountDeleted=true"));
    String html = mvc.perform(get("/login?accountDeleted=true")).andExpect(status().isOk())
        .andReturn().getResponse().getContentAsString();
    assertThat(html).contains("Your account has been deleted. Please sign up again.", "/register");
    mvc.perform(post("/account/login").with(csrf()).param("identifier", "never.registered@sae-malaysia.com")
        .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?error=true"));
    var replacement = accounts.register("replacement.user", email, "Replacement User", "StrongPassword123!");
    replacement.accessStatus = "Approved";
    db.update(replacement);
    var verified = accounts.authenticate(org.springframework.security.authentication.UsernamePasswordAuthenticationToken
        .unauthenticated(email, "StrongPassword123!"));
    assertThat(((Accounts.Principal)verified.getPrincipal()).id()).isEqualTo(replacement.id);
    assertThatThrownBy(() -> accounts.authenticate(org.springframework.security.authentication.UsernamePasswordAuthenticationToken
        .unauthenticated(email, "WrongPassword123!")))
        .isInstanceOf(org.springframework.security.authentication.BadCredentialsException.class)
        .isNotInstanceOf(DeletedAccountException.class);
  }

  @Test void profileChangesAppearOnlyInAdminHistory() throws Exception {
    var admin = account(Accounts.ADMIN);
    var viewer = account(Accounts.USER);
    String id = ((Accounts.Principal)viewer.getPrincipal()).id();
    var u = db.find(my.webgso.Models.ApplicationUser.class,id).orElseThrow();
    profiles.update(id, "Updated User Name", u.userName, u.email, "");
    assertThat(profiles.nameHistory(id)).hasSize(1);
    profiles.update(id, "Updated User Name", u.userName, u.email, "");
    assertThat(profiles.nameHistory(id)).hasSize(1);
    profiles.changeEmail(id, "updated.user@sae-malaysia.com", "StrongPassword123!");
    String html = mvc.perform(get("/admin/registered-users").with(authentication(admin)))
        .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
    assertThat(html).contains("Name changed", "Console User", "Updated User Name", "Email changed", "updated.user@sae-malaysia.com", "Delete user account?");
    assertThat(html.indexOf(">User Management</span>")).isLessThan(html.indexOf(">My Profile</span>"));
    management.manage(admin, id, "delete", "delete");
    assertThat(profiles.nameHistory(id)).isEmpty();
    assertThat(profiles.emailHistory(id)).isEmpty();
  }

  @Test void removedServerControlsHaveNoHandler() throws Exception {
    var admin = account(Accounts.ADMIN);
    mvc.perform(get("/admin/server-controls").with(authentication(admin)))
        .andExpect(status().isNotFound());
    mvc.perform(post("/admin/server-controls/restart").with(authentication(admin)).with(csrf()))
        .andExpect(status().isNotFound());
  }
}
