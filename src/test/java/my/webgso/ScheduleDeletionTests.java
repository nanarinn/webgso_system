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
class ScheduleDeletionTests {
  @Autowired MockMvc mvc;
  @Autowired Store db;
  @Autowired Accounts accounts;
  Authentication auth;
  LssPlant plant;
  CollectionSchedule schedule, other;

  @BeforeEach
  void setup() {
    var user =
        accounts.register(
            "schedule.delete",
            "schedule-delete@example.test",
            "Schedule Admin",
            "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, Accounts.ADMIN);
    auth = accounts.token(user);
    plant = new LssPlant();
    plant.plantId = "SCHEDULE-DELETE";
    plant.name = "Schedule test plant";
    db.insert(plant);
    schedule = new CollectionSchedule();
    schedule.plantId = plant.id;
    schedule.scheduleCode = "DELETE-SCHEDULE";
    schedule.name = "Schedule to delete";
    db.insert(schedule);
    other = new CollectionSchedule();
    other.plantId = plant.id;
    other.scheduleCode = "KEEP-SCHEDULE";
    other.name = "Keep schedule";
    db.insert(other);
  }

  @Test
  void deletionPreservesExistingCollectionsAndOtherSchedules() throws Exception {
    var job = new CollectionJobEntity();
    job.plantId = plant.id;
    job.collectionScheduleId = schedule.id;
    job.status = "Completed";
    db.insert(job);
    mvc.perform(
            post("/admin/collection-schedules/" + schedule.id + "/delete")
                .with(authentication(auth))
                .with(csrf())
                .param("confirmation", "delete"))
        .andExpect(redirectedUrl("/admin/collection-schedules"));
    assertThat(db.find(CollectionSchedule.class, schedule.id)).isEmpty();
    assertThat(db.find(CollectionSchedule.class, other.id)).isPresent();
    assertThat(db.find(LssPlant.class, plant.id)).isPresent();
    assertThat(db.find(CollectionJobEntity.class, job.id).orElseThrow().collectionScheduleId)
        .isNull();
    assertThat(
            db.list(
                AuditEvent.class,
                "WHERE \"Action\"=? AND \"EntityReference\"=?",
                "Delete collection schedule",
                schedule.scheduleCode))
        .hasSize(1);
  }

  @Test
  void runningScheduleCannotBeDeleted() throws Exception {
    var job = new CollectionJobEntity();
    job.plantId = plant.id;
    job.collectionScheduleId = schedule.id;
    job.status = "Running";
    db.insert(job);
    mvc.perform(
            post("/admin/collection-schedules/" + schedule.id + "/delete")
                .with(authentication(auth))
                .with(csrf())
                .param("confirmation", "delete"))
        .andExpect(redirectedUrl("/admin/collection-schedules"))
        .andExpect(
            flash().attribute("message", org.hamcrest.Matchers.containsString("in progress")));
    assertThat(db.find(CollectionSchedule.class, schedule.id)).isPresent();
    assertThat(db.find(CollectionJobEntity.class, job.id).orElseThrow().collectionScheduleId)
        .isEqualTo(schedule.id);
  }

  @Test
  void deletionRequiresSignInCsrfAndConfirmation() throws Exception {
    String path = "/admin/collection-schedules/" + schedule.id + "/delete";
    mvc.perform(post(path).with(authentication(auth)).with(csrf()))
        .andExpect(status().isBadRequest());
    mvc.perform(post(path).with(authentication(auth)).param("confirmation", "delete"))
        .andExpect(status().isForbidden());
    mvc.perform(post(path).with(csrf()).param("confirmation", "delete"))
        .andExpect(status().is3xxRedirection());
    assertThat(db.find(CollectionSchedule.class, schedule.id)).isPresent();
  }

  @Test
  void scheduleListIncludesNamedConfirmationPopup() throws Exception {
    mvc.perform(
            get("/admin/collection-schedules")
                .with(authentication(auth))
                .param("q", schedule.scheduleCode))
        .andExpect(status().isOk())
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString(
                        "Are you sure you want to delete this collection schedule?")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString("Schedule to delete (DELETE-SCHEDULE)")));
  }
}
