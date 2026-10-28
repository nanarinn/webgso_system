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
  @Autowired ApiTestHistoryService testHistory;
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
  }

  @Test
  void noRulesIsShownAsNotValidatedRatherThanClean() throws Exception {
    db.jdbc().update("UPDATE \"ValidationRules\" SET \"IsActive\"=false");
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}"));
    var result = collection.testWithAnomalies(plant.id, "Day Ahead", "configured.admin");
    assertThat(anomalyReports.find(result.reportId()).orElseThrow().rules()).isZero();
  }

  @Test
  void reportPageRequiresAuthenticationAndHandlesMissingReports() throws Exception {
    mvc.perform(get("/admin/anomaly-reports")).andExpect(status().is3xxRedirection());
    mvc.perform(get("/admin/anomaly-reports").param("reportId", "-1").with(authentication(auth)))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    mvc.perform(
            post("/admin/plants/" + plant.id + "/test")
                .param("dataType", "Day Ahead")
                .with(authentication(auth)))
        .andExpect(status().isForbidden());
    verifyNoInteractions(client);
  }

  @Test
  void removedAnomalyPageHasNoNavigationOrReportLinks() throws Exception {
    String html =
        mvc.perform(get("/admin/lss-plants").with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html).doesNotContain("/admin/anomaly-reports", "View anomaly report");
    verifyNoInteractions(client);
  }

  @Test
  void apiPopupShowsHttpTimingAndEscapedRedactedPreview() throws Exception {
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10,\"token\":\"private-token\",\"label\":\"<script>alert(1)</script>\"}",
                125));
    var outcome =
        mvc.perform(
                post("/admin/plants/" + plant.id + "/test")
                    .with(authentication(auth))
                    .with(csrf())
                    .param("dataType", "Day Ahead"))
            .andExpect(redirectedUrl("/admin/lss-plants"))
            .andExpect(flash().attribute("apiTestHttpStatus", 200))
            .andExpect(flash().attribute("apiTestResponseMillis", 125L))
            .andReturn();
    String html =
        mvc.perform(
                get("/admin/lss-plants")
                    .with(authentication(auth))
                    .flashAttrs(outcome.getFlashMap()))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains("HTTP status", "125 ms", "Preview response", "[redacted]", "&lt;script&gt;")
        .doesNotContain("private-token", "<script>alert(1)</script>");
  }

  @Test
  void failedHttpResponseStillHasDiagnosticDetailsAndBoundedPreview() throws Exception {
    when(client.fetch("https://example.test/day-ahead"))
        .thenThrow(
            new PlantApiClient.Failure(
                "HTTP 401: Authentication required.",
                new PlantApiClient.Response(401, "text/plain", "x".repeat(15000), 31)));
    var result =
        mvc.perform(
                post("/admin/plants/" + plant.id + "/test")
                    .with(authentication(auth))
                    .with(csrf())
                    .param("dataType", "Day Ahead"))
            .andExpect(redirectedUrl("/admin/lss-plants"))
            .andExpect(flash().attribute("apiTestHttpStatus", 401))
            .andExpect(flash().attribute("apiTestPreviewTruncated", true))
            .andReturn();
    assertThat((String) result.getFlashMap().get("apiTestPreview")).hasSize(12000);
  }

  @Test
  void historyStoresFailedAndSuccessfulTestsWithLatestStatusAndNoExtraApiCalls() throws Exception {
    when(client.fetch("https://example.test/day-ahead"))
        .thenThrow(
            new PlantApiClient.Failure(
                "HTTP 401: Authentication required.",
                new PlantApiClient.Response(401, "application/json", "{}", 42)))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}",
                123));
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "configured.admin"))
        .isInstanceOf(PlantApiClient.Failure.class);
    collection.test(plant.id, "Day Ahead", "configured.admin");
    assertThat(testHistory.count(plant.id, "Day Ahead")).isEqualTo(2);
    var latest = testHistory.latest().get(ApiTestHistoryService.key(plant.id, "Day Ahead"));
    assertThat(latest.outcome()).isEqualTo("Success");
    assertThat(latest.responseMillis()).isEqualTo(123L);
    String html =
        mvc.perform(
                get("/admin/api-test-history")
                    .param("dataType", "Day Ahead")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains("configured.admin", "Success", "123 ms", "200")
        .containsOnlyOnce(plant.name);
    assertThat(
            testHistory.allHistory(0).stream()
                .filter(test -> test.plantId() == plant.id && test.dataset().equals("Day Ahead")))
        .hasSize(1)
        .allMatch(test -> test.outcome().equals("Success"));
    String table =
        mvc.perform(get("/admin/lss-plants").param("q", plant.plantId).with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(table).contains("api-test-success", "API test history");
    verify(client, times(2)).fetch("https://example.test/day-ahead");
  }

  @Test
  void combinedHistoryIncludesAllPlantsAndDatasetsAndRequiresSignIn() throws Exception {
    long started = System.nanoTime();
    testHistory.save(plant.id, "MMF", "another.admin", false, null, started, "MMF failed");
    assertThat(testHistory.count(plant.id, "Day Ahead")).isZero();
    var another = new LssPlant();
    another.plantId = "HISTORY-OTHER";
    another.name = "Another history plant";
    db.insert(another);
    testHistory.save(
        another.id, "Day Ahead", "configured.admin", true, null, started, "Other plant result");
    String page =
        mvc.perform(
                get("/admin/api-test-history")
                    .param("dataType", "Day Ahead")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(page)
        .contains(
            "MMF failed", "another.admin", "Another history plant", "Day Ahead", "MMF", plant.name);
    mvc.perform(get("/admin/api-test-history").param("dataType", "MMF"))
        .andExpect(status().is3xxRedirection());
    verifyNoInteractions(client);
  }

  @Test
  void historyPaginatesDistinctApisWithoutLosingOlderResults() throws Exception {
    for (int i = 0; i < 26; i++) {
      var other = new LssPlant();
      other.plantId = "HISTORY-PAGE-" + i;
      other.name = "History page plant " + i;
      db.insert(other);
      testHistory.save(
          other.id, "Day Ahead", "configured.admin", true, null, System.nanoTime(), "Result " + i);
    }
    String nextPage =
        mvc.perform(
                get("/admin/api-test-history")
                    .param("dataType", "Day Ahead")
                    .param("page", "1")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(nextPage).contains("Result 0", "Previous", "Page 2").doesNotContain("Result 25");
    verifyNoInteractions(client);
  }
}
