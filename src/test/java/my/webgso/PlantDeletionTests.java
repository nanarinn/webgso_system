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
class PlantDeletionTests {
  @Autowired MockMvc mvc;
  @Autowired Store db;
  @Autowired Accounts accounts;
  Authentication auth;
  LssPlant plant, other;

  @BeforeEach
  void setup() {
    var user =
        accounts.register(
            "delete.admin", "delete@example.test", "Delete Admin", "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, Accounts.ADMIN);
    auth = accounts.token(user);
    plant = new LssPlant();
    plant.plantId = "DELETE-TEST";
    plant.name = "Delete test plant";
    db.insert(plant);
    other = new LssPlant();
    other.plantId = "KEEP-TEST";
    other.name = "Keep test plant";
    db.insert(other);
  }

  @Test
  void deletesOnlySelectedPlantAndConfiguration() throws Exception {
    var api = new PlantApiConfiguration();
    api.plantId = plant.id;
    api.apiType = "MMF";
    api.dataType = "MMF";
    api.apiLink = "https://example.test/mmf";
    db.insert(api);
    var schedule = new CollectionSchedule();
    schedule.plantId = plant.id;
    db.insert(schedule);
    mvc.perform(
            post("/admin/plants/" + plant.id + "/delete")
                .with(authentication(auth))
                .with(csrf())
                .param("confirmation", "delete"))
        .andExpect(redirectedUrl("/admin/lss-plants"))
        .andExpect(flash().attribute("message", "Plant deleted: Delete test plant."));
    assertThat(db.find(LssPlant.class, plant.id)).isEmpty();
    assertThat(db.find(LssPlant.class, other.id)).isPresent();
    assertThat(db.find(PlantApiConfiguration.class, api.id)).isEmpty();
    assertThat(db.find(CollectionSchedule.class, schedule.id)).isEmpty();
    assertThat(
            db.list(
                AuditEvent.class,
                "WHERE \"Action\"=? AND \"EntityReference\"=?",
                "Delete plant",
                plant.plantId))
        .hasSize(1);
  }

  @Test
  void preservesPlantAndHistoryWhenCollectionsExist() throws Exception {
    var job = new CollectionJobEntity();
    job.plantId = plant.id;
    db.insert(job);
    mvc.perform(
            post("/admin/plants/" + plant.id + "/delete")
                .with(authentication(auth))
                .with(csrf())
                .param("confirmation", "delete"))
        .andExpect(redirectedUrl("/admin/lss-plants"))
        .andExpect(
            flash()
                .attribute("message", org.hamcrest.Matchers.containsString("collection history")));
    assertThat(db.find(LssPlant.class, plant.id)).isPresent();
    assertThat(db.find(CollectionJobEntity.class, job.id)).isPresent();
  }

  @Test
  void requiresAuthenticationCsrfAndConfirmation() throws Exception {
    mvc.perform(
            post("/admin/plants/" + plant.id + "/delete").with(authentication(auth)).with(csrf()))
        .andExpect(status().isBadRequest());
    mvc.perform(
            post("/admin/plants/" + plant.id + "/delete")
                .with(authentication(auth))
                .param("confirmation", "delete"))
        .andExpect(status().isForbidden());
    mvc.perform(
            post("/admin/plants/" + plant.id + "/delete")
                .with(csrf())
                .param("confirmation", "delete"))
        .andExpect(status().is3xxRedirection());
    assertThat(db.find(LssPlant.class, plant.id)).isPresent();
  }
}
