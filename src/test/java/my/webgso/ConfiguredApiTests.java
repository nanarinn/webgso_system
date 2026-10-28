package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.util.regex.Pattern;
import my.webgso.Models.*;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.core.Authentication;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@Transactional
class ConfiguredApiTests {
  @Autowired MockMvc mvc;
  @Autowired Store db;
  @Autowired Accounts accounts;
  @Autowired CollectionService collection;
  @Autowired ApiAnomalyService anomalyReports;
  @MockitoBean PlantApiClient client;
  LssPlant plant;
  Authentication auth;

  @BeforeEach
  void setup() {
    reset(client);
    var user =
        accounts.register(
            "configured.admin",
            "configured@example.test",
            "Configured Admin",
            "StrongPassword123!");
    user.accessStatus = "Approved";
    db.update(user);
    accounts.setRole(user.id, Accounts.ADMIN);
    auth = accounts.token(user);
    plant = new LssPlant();
    plant.plantId = "SAVED-API";
    plant.name = "Saved API plant";
    plant.apiConnectionStatus = "Connected";
    db.insert(plant);
    var link = new PlantApiConfiguration();
    link.plantId = plant.id;
    link.apiType = "DDQ";
    link.dataType = "Day Ahead";
    link.apiLink = "https://example.test/day-ahead";
    db.insert(link);
  }

  @Test
  void rowShowsSavedDatasetNamesAndTestsWithoutDropdown() throws Exception {
    String html =
        mvc.perform(get("/admin/lss-plants").param("q", plant.plantId).with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    var form =
        Pattern.compile(
                "<form[^>]*action=\"/admin/plants/" + plant.id + "/test\"[^>]*>(.*?)</form>",
                Pattern.DOTALL)
            .matcher(html);
    assertThat(form.find()).isTrue();
    assertThat(form.group(1))
        .contains("value=\"Day Ahead\"", "type=\"hidden\"")
        .doesNotContain("<select", "value=\"Rolling 24 Hour\"", "value=\"MMF\"");
    assertThat(html).contains("plant-api-dataset-button\">Day Ahead</button>");
    assertThat(html)
        .contains("plant-table-api-test", "Test Day Ahead API for Saved API plant")
        .doesNotContain("<th>Status</th>", "plant-status-toggle", "/status\"");
  }

  @Test
  void testUsesTheSavedDatasetUrl() {
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}"));
    collection.test(plant.id, "Day Ahead", "configured.admin");
    verify(client).fetch("https://example.test/day-ahead");
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().apiConnectionStatus)
        .isEqualTo("Connected");
  }

  @Test
  void wrongDatasetDoesNotFetchOrOverwriteSuccessfulConnectionState() {
    assertThatThrownBy(() -> collection.test(plant.id, "Rolling 24 Hour", "configured.admin"))
        .isInstanceOf(IllegalArgumentException.class)
        .hasMessageContaining("for Rolling 24 Hour");
    verifyNoInteractions(client);
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().apiConnectionStatus)
        .isEqualTo("Connected");
  }

  @Test
  void apiTestPersistsFindingsWithoutCollectingReadings() throws Exception {
    var rules = db.list(ValidationRule.class, "WHERE \"Code\"='DDQ-004'");
    var rule = rules.isEmpty() ? new ValidationRule() : rules.getFirst();
    rule.code = "DDQ-004";
    rule.dataType = "DDQ";
    rule.isActive = true;
    if (rule.id == 0) db.insert(rule);
    else db.update(rule);
    long before = db.count(SolarApiReading.class);
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":-10}"));
    var result = collection.testWithAnomalies(plant.id, "Day Ahead", "configured.admin");
    var report = anomalyReports.find(result.reportId()).orElseThrow();
    assertThat(report.plantName()).isEqualTo(plant.name);
    assertThat(report.dataset()).isEqualTo("Day Ahead");
    assertThat(report.issues()).anyMatch(issue -> issue.code().equals("DDQ-004"));
    assertThat(db.count(SolarApiReading.class)).isEqualTo(before);
    verify(client, times(1)).fetch("https://example.test/day-ahead");
    String html =
        mvc.perform(
                get("/admin/anomaly-reports")
                    .param("reportId", "" + report.id())
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains("Anomalies detected", "Negative forecast value", "Day Ahead", "Saved API plant");
  }

  @Test
  void noRulesIsShownAsNotValidatedRatherThanClean() throws Exception {
    db.jdbc().update("UPDATE \"ValidationRules\" SET \"IsActive\"=false");
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}"));
    var result = collection.testWithAnomalies(plant.id, "Day Ahead", "configured.admin");
    String html =
        mvc.perform(
                get("/admin/anomaly-reports")
                    .param("reportId", "" + result.reportId())
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains("Not validated", "No active validation rules")
        .doesNotContain("No anomalies detected");
  }

  @Test
  void reportPageRequiresAuthenticationAndHandlesMissingReports() throws Exception {
    mvc.perform(get("/admin/anomaly-reports")).andExpect(status().is3xxRedirection());
    mvc.perform(get("/admin/anomaly-reports").param("reportId", "-1").with(authentication(auth)))
        .andExpect(status().isNotFound());
    mvc.perform(
            post("/admin/plants/" + plant.id + "/test")
                .param("dataType", "Day Ahead")
                .with(authentication(auth)))
        .andExpect(status().isForbidden());
    verifyNoInteractions(client);
  }
}
