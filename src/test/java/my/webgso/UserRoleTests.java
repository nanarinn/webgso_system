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

@SpringBootTest(properties = {
    "spring.datasource.url=jdbc:h2:mem:webgso-user-roles;MODE=PostgreSQL;DB_CLOSE_DELAY=-1;DATABASE_TO_UPPER=false",
    "webgso.accounts.signup-emails=new.user@sae-malaysia.com"
})
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class UserRoleTests {
  @Autowired Accounts accounts;
  @Autowired Store db;
  @Autowired MockMvc mvc;

  private org.springframework.security.core.Authentication viewer() {
    var user = accounts.register("viewer.role", "viewer@sae-malaysia.com", "Viewer", "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, Accounts.USER);
    assertThat(accounts.roles(user.id)).containsExactly(Accounts.USER);
    return accounts.token(user);
  }

  @Test void userCanViewAndTestButCannotManage() throws Exception {
    var auth = viewer();
    for (String path : new String[]{"/", "/user/lss", "/user/plant-config", "/user/ddq-test", "/user/mmf-test", "/account/profile"})
      mvc.perform(get(path).with(authentication(auth))).andExpect(status().isOk());
    String html = mvc.perform(get("/").with(authentication(auth))).andReturn().getResponse().getContentAsString();
    assertThat(html).contains("User workspace", "/user/ddq-test").doesNotContain("href=\"/admin/");
    var plant = new Models.LssPlant();
    plant.plantId = "01";
    plant.name = "User visible plant";
    plant.mmfApiEndpoint = "https://example.com/mmf";
    db.insert(plant);
    String plants = mvc.perform(get("/user/plant-config").with(authentication(auth)))
        .andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
    assertThat(plants).contains("Plant Config", "User visible plant", "https://example.com/mmf", "/user/mmf-test", "Test API")
        .doesNotContain("href=\"/admin/", "Add record", ">Delete<", ">Edit<");
    for (String path : new String[]{"/admin/lss-plants", "/admin/lss-plants/edit", "/admin/api-test-history", "/api/admin/plants"})
      mvc.perform(get(path).with(authentication(auth))).andExpect(status().isForbidden());
    for (String path : new String[]{"/admin/lss-plants/save", "/admin/plants/1/delete", "/admin/plants/1/test", "/api/admin/plants"})
      mvc.perform(post(path).with(authentication(auth)).with(csrf())).andExpect(status().isForbidden());
    mvc.perform(post("/user/ddq-test").with(authentication(auth)).with(csrf())
        .param("apiUrl", "invalid").param("dataType", "Rolling 24 Hour"))
        .andExpect(status().isOk()).andExpect(view().name("api-quick-test"));
    mvc.perform(post("/user/mmf-test").with(authentication(auth)))
        .andExpect(status().is3xxRedirection());
  }

  @Test void publicSignupDoesNotGrantAdmin() {
    var user = accounts.registerSaeEmail("new.user@sae-malaysia.com", "StrongPassword123!");
    assertThat(accounts.roles(user.id)).containsExactly(Accounts.USER);
  }
}
