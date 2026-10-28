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
import org.springframework.security.core.Authentication;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class PlantStatusTests {
  @Autowired MockMvc mvc;
  @Autowired Store db;
  @Autowired Accounts accounts;
  Authentication auth;
  LssPlant plant;

  @BeforeEach
  void setup() {
    var user =
        accounts.register(
            "status.admin", "status@example.test", "Status Admin", "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, Accounts.ADMIN);
    auth = accounts.token(user);
    plant = new LssPlant();
    plant.plantId = "STATUS-TEST";
    plant.name = "Status plant";
    plant.ddqApiEndpoint = "https://example.test/unchanged";
    db.insert(plant);
  }

  @Test
  void togglesBothWaysWithoutChangingOtherFields() throws Exception {
    for (String status : new String[] {"Inactive", "Active"}) {
      mvc.perform(
              post("/admin/plants/" + plant.id + "/status")
                  .with(authentication(auth))
                  .with(csrf())
                  .param("status", status))
          .andExpect(redirectedUrl("/admin/lss-plants"))
          .andExpect(flash().attribute("message", "Plant status saved: " + status + "."));
      var saved = db.find(LssPlant.class, plant.id).orElseThrow();
      assertThat(saved.isActive).isEqualTo(status.equals("Active"));
      assertThat(saved.ddqApiEndpoint).isEqualTo(plant.ddqApiEndpoint);
      assertThat(saved.name).isEqualTo(plant.name);
    }
    assertThat(
            db.list(
                AuditEvent.class,
                "WHERE \"Action\"=? AND \"EntityReference\"=?",
                "Update plant status",
                plant.id.toString()))
        .hasSize(2);
  }

  @Test
  void rejectsInvalidStatusAndMissingPlant() throws Exception {
    mvc.perform(
            post("/admin/plants/" + plant.id + "/status")
                .with(authentication(auth))
                .with(csrf())
                .param("status", "anything"))
        .andExpect(status().isBadRequest());
    mvc.perform(
            post("/admin/plants/2147483647/status")
                .with(authentication(auth))
                .with(csrf())
                .param("status", "Inactive"))
        .andExpect(status().isBadRequest());
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().isActive).isTrue();
  }

  @Test
  void requiresSignInAndCsrf() throws Exception {
    mvc.perform(
            post("/admin/plants/" + plant.id + "/status")
                .with(authentication(auth))
                .param("status", "Inactive"))
        .andExpect(status().isForbidden());
    mvc.perform(
            post("/admin/plants/" + plant.id + "/status").with(csrf()).param("status", "Inactive"))
        .andExpect(status().is3xxRedirection());
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().isActive).isTrue();
  }
}
