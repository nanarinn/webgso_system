package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.util.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.mock.env.MockEnvironment;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class ApiIssueEmailsTests {
  @Autowired Store db;
  @Autowired Accounts accounts;
  @Autowired ApiTestHistoryService history;
  @Autowired EmailPreferences prefs;
  @Autowired MockMvc mvc;
  ApiIssueEmails service;
  ApplicationUser user, pending;
  LssPlant plant;

  @BeforeEach
  void setup() {
    user =
        accounts.register("issue.user", "issue.user@gmail.com", "Issue user", "StrongPassword123!");
    user.accessStatus = "Approved";
    user.emailConfirmed = true;
    db.update(user);
    pending =
        accounts.register(
            "issue.pending", "issue.pending@gmail.com", "Pending", "StrongPassword123!");
    plant = new LssPlant();
    plant.plantId = "ISSUE-01";
    plant.name = "Email test plant";
    db.insert(plant);
    for (String dataset : List.of("Day Ahead", "Week Ahead", "Rolling 24 Hour")) {
      var api = new PlantApiConfiguration();
      api.plantId = plant.id;
      api.apiType = "DDQ";
      api.dataType = dataset;
      api.apiLink = "https://example.test/forecast";
      db.insert(api);
    }
    service =
        new ApiIssueEmails(
            db,
            history,
            accounts,
            new MockEnvironment().withProperty("webgso.mail.enabled", "true"));
  }

  void result(String dataset, boolean success, String message) {
    history.save(plant.id, dataset, user.email, success, null, System.nanoTime(), message);
  }

  @Test
  void onlyLatestConfiguredFailedOrEmptyResultsAreSelectable() {
    result("Day Ahead", false, "Failed");
    result("Day Ahead", true, "10 readable values");
    result("Week Ahead", false, "Connection failed");
    result("Rolling 24 Hour", true, "0 readable values");
    result("MMF", false, "Not configured");
    assertThat(service.issues())
        .extracting(i -> i.test().dataset())
        .containsExactlyInAnyOrder("Week Ahead", "Rolling 24 Hour");
    assertThat(service.issues()).extracting(ApiIssueEmails.Issue::reason).contains("No API values");
  }

  @Test
  void queuesOnlySelectedRecipientsAndDeduplicatesWithoutLeakingPayload() {
    result("Week Ahead", false, "Secret payload https://example.test/?token=hidden");
    long id = service.issues().getFirst().test().id();
    assertThat(service.queue(List.of(id), List.of(user.id), user.id)).isEqualTo(1);
    assertThat(service.queue(List.of(id, id), List.of(user.id, user.id), user.id)).isZero();
    var notes =
        db.list(UserAccessNotification.class, "WHERE \"NotificationType\"='ApiIssueMessage'");
    assertThat(notes).hasSize(1);
    var note = notes.getFirst();
    assertThat(note.email).isEqualTo(user.email);
    assertThat(note.deliveryStatus).isEqualTo("Pending");
    assertThat(note.body)
        .contains("Email test plant", "Week Ahead", "API test failed")
        .doesNotContain("token=hidden", "Secret payload");
    assertThat(prefs.allowsDelivery(note)).isTrue();
    user.email = "changed@gmail.com";
    db.update(user);
    assertThat(prefs.allowsDelivery(note)).isFalse();
  }

  @Test
  void rejectsUnregisteredUnverifiedAndStaleSelectionsBeforeQueuing() {
    result("Day Ahead", false, "Failed");
    long id = service.issues().getFirst().test().id();
    assertThat(service.recipients())
        .extracting(u -> u.id)
        .contains(user.id)
        .doesNotContain(pending.id);
    assertThatThrownBy(() -> service.queue(List.of(id), List.of(pending.id), user.id))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> service.queue(List.of(id), List.of("outside@gmail.com"), user.id))
        .isInstanceOf(IllegalArgumentException.class);
    result("Day Ahead", true, "10 readable values");
    assertThatThrownBy(() -> service.queue(List.of(id), List.of(user.id), user.id))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(
            db.list(UserAccessNotification.class, "WHERE \"NotificationType\"='ApiIssueMessage'"))
        .isEmpty();
  }

  @Test
  void pageAndSendRequireAuthenticationAndCsrfAndShowRegisteredRecipients() throws Exception {
    mvc.perform(get("/admin/api-email")).andExpect(redirectedUrl("/login"));
    var auth = accounts.token(user);
    mvc.perform(get("/admin/api-email").with(authentication(auth)))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Send email")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString(user.email)))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString(pending.email))));
    mvc.perform(post("/admin/api-email").with(authentication(auth)))
        .andExpect(status().isForbidden());
  }
}
