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
  @Autowired org.springframework.jdbc.core.JdbcTemplate jdbc;
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
    var schedule = new CollectionSchedule();
    schedule.plantId = plant.id;
    schedule.dataType = "Day Ahead";
    schedule.cronExpression = "0 0 0 * * *";
    db.insert(schedule);
  }

  @Test
  void locationFiltersApplyOneGroupAndKeepSelectionsAcrossPages() throws Exception {
    plant.state = "PERAK";
    plant.zone = "West";
    db.update(plant);
    // A stale selection in a collapsed group must never restrict the chosen group.
    var response =
        mvc.perform(
                get("/admin/lss-plants")
                    .param("q", plant.plantId)
                    .param("locationMode", "zone")
                    .param("locationZones", "West")
                    .param("locationStates", "Johor")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn();
    assertThat(response.getModelAndView().getModel().get("total")).isEqualTo(1L);
    assertThat(response.getModelAndView().getModel().get("locationStates")).isEqualTo("");
    assertThat(response.getResponse().getContentAsString())
        .contains("Plant configuration filters", "location-mode-region", "location-mode-zone")
        .contains("name=\"locationZones\" type=\"checkbox\" value=\"West\" checked=\"checked\"");
    String lockedHtml = response.getResponse().getContentAsString();
    for (String group : java.util.List.of("plant", "state", "region", "all")) {
      assertThat(lockedHtml)
          .containsPattern("id=\"location-mode-" + group + "\"[^>]*disabled=\"disabled\"");
    }
    assertThat(lockedHtml)
        .doesNotContain(">Locked</span>")
        .contains("Remove Zone filter", "/scripts/plant-location-filters.js?v=2")
        .contains(
            "class=\"location-values\" disabled=\"disabled\" hidden=\"hidden\" aria-label=\"State"
                + " selections\"");
    var unlocked =
        mvc.perform(
                get("/admin/lss-plants").param("locationMode", "all").with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(unlocked).doesNotContainPattern("id=\"location-mode-state\"[^>]*disabled");
    var excluded =
        mvc.perform(
                get("/admin/lss-plants")
                    .param("q", plant.plantId)
                    .param("locationMode", "region")
                    .param("locationRegions", "Southern")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn();
    assertThat(excluded.getModelAndView().getModel().get("total")).isEqualTo(0L);
    var included =
        mvc.perform(
                get("/admin/lss-plants")
                    .param("q", plant.plantId)
                    .param("locationMode", "state")
                    .param("locationStates", "Johor", "Perak")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn();
    assertThat(included.getModelAndView().getModel().get("total")).isEqualTo(1L);
    assertThat(included.getModelAndView().getModel().get("locationStates"))
        .isEqualTo("Johor,Perak");
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
  void quickTestDoesNotSavePlantsLinksReadingsHistoryOrNotifications() throws Exception {
    long plants = db.count(LssPlant.class),
        links = db.count(PlantApiConfiguration.class),
        readings = db.count(SolarApiReading.class),
        tests = testHistory.countAll(),
        notifications = db.count(UserAccessNotification.class);
    when(client.fetch("https://example.test/quick"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-30T08:00\",\"value\":2,\"token\":\"secret-value\"}"));
    mvc.perform(
            post("/admin/api-quick-test")
                .with(authentication(auth))
                .with(csrf())
                .param("apiUrl", "https://example.test/quick")
                .param("dataType", "Day Ahead"))
        .andExpect(status().isOk())
        .andExpect(view().name("api-quick-test"))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Connection successful")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("secret-value"))));
    assertThat(db.count(LssPlant.class)).isEqualTo(plants);
    assertThat(db.count(PlantApiConfiguration.class)).isEqualTo(links);
    assertThat(db.count(SolarApiReading.class)).isEqualTo(readings);
    assertThat(testHistory.countAll()).isEqualTo(tests);
    assertThat(db.count(UserAccessNotification.class)).isEqualTo(notifications);
    when(client.fetch("https://example.test/quick"))
        .thenThrow(new IllegalArgumentException("API request timed out."));
    mvc.perform(
            post("/admin/api-quick-test")
                .with(authentication(auth))
                .with(csrf())
                .param("apiUrl", "https://example.test/quick")
                .param("dataType", "MMF"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("API test failed")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("timed out")));
    assertThat(testHistory.countAll()).isEqualTo(tests);
    assertThat(db.count(UserAccessNotification.class)).isEqualTo(notifications);
  }

  @Test
  void quickTestRequiresCsrfAndRejectsUnknownDatasetsBeforeFetching() throws Exception {
    mvc.perform(
            post("/admin/api-quick-test")
                .with(authentication(auth))
                .param("apiUrl", "https://example.test/quick")
                .param("dataType", "MMF"))
        .andExpect(status().isForbidden());
    mvc.perform(
            post("/admin/api-quick-test")
                .with(csrf())
                .param("apiUrl", "https://example.test/quick")
                .param("dataType", "MMF"))
        .andExpect(status().is3xxRedirection());
    mvc.perform(
            post("/admin/api-quick-test")
                .with(authentication(auth))
                .with(csrf())
                .param("apiUrl", "https://example.test/quick")
                .param("dataType", "unknown"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Choose a supported")));
    verifyNoInteractions(client);
  }

  @Test
  void plantFiltersUseConfiguredApisAndPreserveSelectedValues() throws Exception {
    mvc.perform(
            get("/admin/lss-plants")
                .with(authentication(auth))
                .param("filterPlant", "" + plant.id)
                .param("filterType", "DDQ")
                .param("filterResult", "Not tested"))
        .andExpect(status().isOk())
        .andExpect(model().attribute("total", 1L))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Apply filters")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("name=\"filterResult\"")));
    mvc.perform(
            get("/admin/lss-plants")
                .with(authentication(auth))
                .param("filterPlant", "" + plant.id)
                .param("filterType", "MMF"))
        .andExpect(status().isOk())
        .andExpect(model().attribute("total", 0L));
    testHistory.save(plant.id, "Day Ahead", "admin", false, null, System.nanoTime(), "Timeout");
    mvc.perform(
            get("/admin/lss-plants")
                .with(authentication(auth))
                .param("filterPlant", "" + plant.id)
                .param("filterResult", "Failed"))
        .andExpect(status().isOk())
        .andExpect(model().attribute("total", 1L));
    mvc.perform(
            get("/admin/lss-plants")
                .with(authentication(auth))
                .param("filterPlant", "" + plant.id)
                .param("q", "no-such-name"))
        .andExpect(status().isOk())
        .andExpect(model().attribute("total", 0L));
  }

  @Test
  void http200WithBadStructureIsFailedWithoutImportingReadings() {
    long readings = db.count(SolarApiReading.class);
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "[{\"timestamp\":\"2026-10-28T00:00\",\"value\":1},{\"timestamp\":\"bad\",\"value\":2}]"));
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "configured.admin"))
        .isInstanceOf(PlantApiClient.Failure.class)
        .hasMessageContaining("timestamp");
    assertThat(db.count(SolarApiReading.class)).isEqualTo(readings);
    var latest = testHistory.latest().get(ApiTestHistoryService.key(plant.id, "Day Ahead"));
    assertThat(latest.outcome()).isEqualTo("Failed");
    assertThat(latest.httpStatus()).isEqualTo(200);
    assertThat(latest.message()).contains("structure is invalid");
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
  void expiredForecastRemainsConnectedAndWarningIsSavedAndShown() throws Exception {
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"2001-01-01T00:00:00\",\"power\":10}"));
    mvc.perform(
            post("/admin/plants/" + plant.id + "/test")
                .param("dataType", "Day Ahead")
                .with(authentication(auth))
                .with(csrf()))
        .andExpect(status().is3xxRedirection())
        .andExpect(
            flash()
                .attribute(
                    "apiTestFreshnessWarning",
                    org.hamcrest.Matchers.containsString("Expired forecast")));
    var latest = testHistory.latest().get(ApiTestHistoryService.key(plant.id, "Day Ahead"));
    assertThat(latest.outcome()).isEqualTo("Success");
    assertThat(latest.message()).contains("Warning: Expired forecast");
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().apiConnectionStatus)
        .isEqualTo("Connected");
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
  void failedFirstTestAlsoBlocksFurtherApiRequests() throws Exception {
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
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "configured.admin"))
        .isInstanceOf(ApiTestHistoryService.AlreadyTested.class);
    assertThat(testHistory.count(plant.id, "Day Ahead")).isEqualTo(1);
    var latest = testHistory.latest().get(ApiTestHistoryService.key(plant.id, "Day Ahead"));
    assertThat(latest.outcome()).isEqualTo("Failed");
    assertThat(latest.responseMillis()).isEqualTo(42L);
    String html =
        mvc.perform(
                get("/admin/api-test-history")
                    .param("dataType", "Day Ahead")
                    .with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html).contains("configured.admin", "Failed", "42 ms", "401");
    assertThat(
            Pattern.compile("<tr>.*?</tr>", Pattern.DOTALL)
                .matcher(html)
                .results()
                .filter(match -> match.group().contains(plant.name))
                .count())
        .isEqualTo(1);
    assertThat(
            testHistory.allHistory(0).stream()
                .filter(test -> test.plantId() == plant.id && test.dataset().equals("Day Ahead")))
        .hasSize(1)
        .allMatch(test -> test.outcome().equals("Failed"));
    String table =
        mvc.perform(get("/admin/lss-plants").param("q", plant.plantId).with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(table).contains("api-test-failed", "API test history");
    verify(client, times(1)).fetch("https://example.test/day-ahead");
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

  @Test
  void repeatedTestShowsAlreadyTestedPopupAndDoesNotCallApiOrAddHistory() throws Exception {
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}",
                123));
    mvc.perform(
            post("/admin/plants/" + plant.id + "/test")
                .with(authentication(auth))
                .with(csrf())
                .param("dataType", "Day Ahead"))
        .andExpect(flash().attribute("apiTestHttpStatus", 200));
    var repeated =
        mvc.perform(
                post("/admin/plants/" + plant.id + "/test")
                    .with(authentication(auth))
                    .with(csrf())
                    .param("dataType", "Day Ahead"))
            .andExpect(redirectedUrl("/admin/lss-plants"))
            .andExpect(flash().attribute("apiTestTitle", "Please wait before testing again"))
            .andExpect(flash().attribute("apiTestAlreadyTested", true))
            .andReturn();
    String html =
        mvc.perform(
                get("/admin/lss-plants")
                    .with(authentication(auth))
                    .flashAttrs(repeated.getFlashMap()))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains("Please wait before testing again", "This follows Collection Schedules")
        .doesNotContain("HTTP status", "Preview response");
    assertThat(testHistory.count(plant.id, "Day Ahead")).isEqualTo(1);
    verify(client, times(1)).fetch("https://example.test/day-ahead");
  }

  @Test
  void testsSavedBeforeOneTimePolicyAreAlsoBlocked() {
    testHistory.save(
        plant.id, "Day Ahead", "previous.admin", true, null, System.nanoTime(), "Existing test");
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "configured.admin"))
        .isInstanceOf(ApiTestHistoryService.AlreadyTested.class);
    assertThat(testHistory.count(plant.id, "Day Ahead")).isEqualTo(1);
    verifyNoInteractions(client);
  }

  @Test
  void explicitHistoryRecheckUpdatesLatestResultButRegularTestStillCannotRepeat() {
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}",
                25))
        .thenThrow(
            new PlantApiClient.Failure(
                "HTTP 503: Server unavailable",
                new PlantApiClient.Response(503, "application/json", "{}", 40)));
    collection.test(plant.id, "Day Ahead", "configured.admin");
    assertThatThrownBy(
            () -> collection.retestWithAnomalies(plant.id, "Day Ahead", "configured.admin"))
        .isInstanceOf(PlantApiClient.Failure.class);
    var latest = testHistory.latest().get(ApiTestHistoryService.key(plant.id, "Day Ahead"));
    assertThat(latest.outcome()).isEqualTo("Failed");
    assertThat(latest.httpStatus()).isEqualTo(503);
    assertThat(
            testHistory.allHistory(0).stream()
                .filter(item -> item.plantId() == plant.id && item.dataset().equals("Day Ahead")))
        .hasSize(1);
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "configured.admin"))
        .isInstanceOf(ApiTestHistoryService.AlreadyTested.class);
    verify(client, times(2)).fetch("https://example.test/day-ahead");
  }

  @Test
  void testAllRequiresAuthenticationAndCsrf() throws Exception {
    mvc.perform(post("/admin/lss-plants/checks/example/email").with(authentication(auth)))
        .andExpect(status().isForbidden());
    mvc.perform(post("/admin/lss-plants/checks/example/email").with(csrf()))
        .andExpect(status().is3xxRedirection());
    mvc.perform(post("/admin/lss-plants/test-all").with(authentication(auth)))
        .andExpect(status().isForbidden());
    mvc.perform(post("/admin/lss-plants/test-all").with(csrf()))
        .andExpect(status().is3xxRedirection());
    mvc.perform(get("/admin/lss-plants").with(authentication(auth)))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("/admin/lss-plants/test-all")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Test all APIs")));
    mvc.perform(
            post("/admin/api-test-history/retest")
                .with(authentication(auth))
                .param("plantId", "1")
                .param("dataType", "MMF"))
        .andExpect(status().isForbidden());
    mvc.perform(
            post("/admin/api-test-history/retest")
                .with(csrf())
                .param("plantId", "1")
                .param("dataType", "MMF"))
        .andExpect(status().is3xxRedirection());
    mvc.perform(post("/admin/api-test-history/test-all").with(authentication(auth)))
        .andExpect(status().isForbidden());
    mvc.perform(post("/admin/api-test-history/test-all").with(csrf()))
        .andExpect(status().is3xxRedirection());
    verifyNoInteractions(client);
  }

  @Test
  void historyOffersTestAgainForSuccessAndFailedApis() throws Exception {
    testHistory.save(plant.id, "Day Ahead", "admin", false, null, System.nanoTime(), "Timeout");
    testHistory.save(plant.id, "MMF", "admin", true, null, System.nanoTime(), "Passed");
    String html =
        mvc.perform(get("/admin/api-test-history").with(authentication(auth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html).contains("Test API again", "/admin/api-test-history/retest");
    var ownRows =
        Pattern.compile("<tr>.*?</tr>", Pattern.DOTALL)
            .matcher(html)
            .results()
            .map(java.util.regex.MatchResult::group)
            .filter(row -> row.contains("Saved API plant"))
            .toList();
    assertThat(ownRows).hasSize(2);
    assertThat(ownRows.stream().filter(row -> row.contains("Failed")).findFirst().orElseThrow())
        .contains("Test API again", "value=\"Day Ahead\"");
    assertThat(ownRows.stream().filter(row -> row.contains("Success")).findFirst().orElseThrow())
        .contains("Test API again", "value=\"MMF\"");
    verifyNoInteractions(client);
  }

  @Test
  void historyRetestReturnsResultPopupAndKeepsHistoryPage() throws Exception {
    testHistory.save(plant.id, "Day Ahead", "admin", true, null, System.nanoTime(), "Previous");
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}",
                25))
        .thenThrow(
            new PlantApiClient.Failure(
                "HTTP 503: unavailable",
                new PlantApiClient.Response(503, "text/plain", "unavailable", 40)));
    var success =
        mvc.perform(
                post("/admin/api-test-history/retest")
                    .with(authentication(auth))
                    .with(csrf())
                    .param("plantId", String.valueOf(plant.id))
                    .param("dataType", "Day Ahead")
                    .param("page", "1"))
            .andExpect(redirectedUrl("/admin/api-test-history?page=1"))
            .andExpect(flash().attribute("apiTestTitle", "Connection successful"))
            .andExpect(flash().attribute("apiTestHttpStatus", 200))
            .andReturn();
    String html =
        mvc.perform(
                get("/admin/api-test-history")
                    .param("page", "1")
                    .with(authentication(auth))
                    .flashAttrs(success.getFlashMap()))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains(
            "<dialog",
            "api-test-popup",
            "Connection successful",
            "method=\"dialog\"",
            ">OK</button>");
    var failed =
        mvc.perform(
                post("/admin/api-test-history/retest")
                    .with(authentication(auth))
                    .with(csrf())
                    .param("plantId", String.valueOf(plant.id))
                    .param("dataType", "Day Ahead"))
            .andExpect(redirectedUrl("/admin/api-test-history?page=0"))
            .andExpect(flash().attribute("apiTestTitle", "Connection test failed"))
            .andExpect(flash().attribute("apiTestHttpStatus", 503))
            .andReturn();
    String failureHtml =
        mvc.perform(
                get("/admin/api-test-history")
                    .with(authentication(auth))
                    .flashAttrs(failed.getFlashMap()))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(failureHtml).contains("api-test-popup", "HTTP 503: unavailable", "Test API again");
    assertThat(testHistory.allHistory(0).stream().filter(item -> item.plantId() == plant.id))
        .hasSize(1);
    verify(client, times(2)).fetch("https://example.test/day-ahead");
  }

  @Test
  void plantTestAllowedAgainAfterNextScheduledRun() {
    testHistory.save(plant.id, "Day Ahead", "admin", true, null, System.nanoTime(), "Previous");
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "admin"))
        .isInstanceOf(ApiTestHistoryService.AlreadyTested.class);
    verifyNoInteractions(client);
    jdbc.update(
        "UPDATE \"ApiTestHistory\" SET \"TestedAt\"=? WHERE \"PlantId\"=?",
        java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC).minusDays(2),
        plant.id);
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}",
                25));
    collection.test(plant.id, "Day Ahead", "admin");
    assertThat(testHistory.count(plant.id, "Day Ahead")).isEqualTo(2);
    verify(client).fetch("https://example.test/day-ahead");
  }

  @Test
  void noActiveScheduleAllowsRetestButConcurrentClaimsAreBlocked() {
    jdbc.update(
        "UPDATE \"CollectionSchedules\" SET \"IsActive\"=false WHERE \"PlantId\"=?", plant.id);
    when(client.fetch("https://example.test/day-ahead"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"timestamp\":\"2026-10-28T00:00:00\",\"power\":10}",
                25));
    collection.test(plant.id, "Day Ahead", "admin");
    collection.test(plant.id, "Day Ahead", "admin");
    verify(client, times(2)).fetch("https://example.test/day-ahead");
    assertThat(testHistory.claim(plant.id, "Day Ahead")).isTrue();
    assertThat(testHistory.claim(plant.id, "Day Ahead")).isFalse();
    assertThatThrownBy(() -> collection.test(plant.id, "Day Ahead", "admin"))
        .isInstanceOf(ApiTestHistoryService.AlreadyTested.class);
    testHistory.release(plant.id, "Day Ahead");
  }

  @Test
  void historyFiltersLatestResultsAndUsesInclusiveMalaysiaDates() {
    testHistory.save(plant.id, "Day Ahead", "admin", false, null, System.nanoTime(), "Old failure");
    testHistory.save(
        plant.id, "Day Ahead", "admin", true, null, System.nanoTime(), "Current success");
    testHistory.save(plant.id, "MMF", "admin", false, null, System.nanoTime(), "Previous MYT day");
    jdbc.update(
        "UPDATE \"ApiTestHistory\" SET \"TestedAt\"=? WHERE \"PlantId\"=? AND \"Dataset\"=?",
        java.time.OffsetDateTime.parse("2026-10-28T16:00:00Z"),
        plant.id,
        "Day Ahead");
    jdbc.update(
        "UPDATE \"ApiTestHistory\" SET \"TestedAt\"=? WHERE \"PlantId\"=? AND \"Dataset\"=?",
        java.time.OffsetDateTime.parse("2026-10-28T15:59:59Z"),
        plant.id,
        "MMF");
    var date = java.time.LocalDate.parse("2026-10-29");
    var filter = new ApiTestHistoryService.Filter(plant.id, "Day Ahead", "Success", date, date);
    assertThat(testHistory.allHistory(0, filter))
        .extracting(ApiTestHistoryService.Entry::message)
        .containsExactly("Current success");
    assertThat(testHistory.countAll(filter)).isEqualTo(1);
    assertThat(
            testHistory.countAll(
                new ApiTestHistoryService.Filter(plant.id, "Day Ahead", "Failed", null, null)))
        .isZero();
    assertThat(
            testHistory.countAll(
                new ApiTestHistoryService.Filter(plant.id, "MMF", "Failed", date, date)))
        .isZero();
    assertThat(
            testHistory.countAll(
                new ApiTestHistoryService.Filter(plant.id, "x' OR 1=1 --", "", null, null)))
        .isZero();
    jdbc.update(
        "UPDATE \"ApiTestHistory\" SET \"TestedAt\"=? WHERE \"PlantId\"=? AND \"Dataset\"=?",
        java.time.OffsetDateTime.parse("2026-10-29T15:59:59Z"),
        plant.id,
        "Day Ahead");
    assertThat(testHistory.countAll(filter)).isEqualTo(1);
    jdbc.update(
        "UPDATE \"ApiTestHistory\" SET \"TestedAt\"=? WHERE \"PlantId\"=? AND \"Dataset\"=?",
        java.time.OffsetDateTime.parse("2026-10-29T16:00:00Z"),
        plant.id,
        "Day Ahead");
    assertThat(testHistory.countAll(filter)).isZero();
  }

  @Test
  void historyCsvRespectsFiltersAndExportsBeyondTheCurrentPage() throws Exception {
    for (int i = 0; i < 26; i++)
      testHistory.save(
          plant.id, "CSV-API-" + i, "admin", true, null, System.nanoTime(), "CSV row " + i);
    testHistory.save(
        plant.id, "Day Ahead", "admin", false, null, System.nanoTime(), "Old failed result");
    testHistory.save(
        plant.id, "Day Ahead", "admin", true, null, System.nanoTime(), "Latest matching result");
    var csv =
        mvc.perform(
                get("/admin/api-test-history/export")
                    .with(authentication(auth))
                    .param("filterPlant", plant.id.toString()))
            .andExpect(status().isOk())
            .andExpect(
                header()
                    .string(
                        "Content-Disposition", "attachment; filename=\"gso-api-test-history.csv\""))
            .andReturn()
            .getResponse()
            .getContentAsString(java.nio.charset.StandardCharsets.UTF_8);
    assertThat(csv)
        .contains("CSV row 25", "CSV row 0", "Latest matching result")
        .doesNotContain("Old failed result");
    mvc.perform(
            get("/admin/api-test-history/export")
                .with(authentication(auth))
                .param("filterPlant", plant.id.toString())
                .param("filterApi", "Day Ahead")
                .param("filterResult", "Failed"))
        .andExpect(status().isOk())
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Latest matching result"))));
    mvc.perform(
            get("/admin/api-test-history/export")
                .with(authentication(auth))
                .param("dateFrom", "2026-11-02")
                .param("dateTo", "2026-11-01"))
        .andExpect(status().isBadRequest());
    mvc.perform(get("/admin/api-test-history/export")).andExpect(status().is3xxRedirection());
  }

  @Test
  void historyFilterFormKeepsSelectionsAndRendersEmptyState() throws Exception {
    testHistory.save(plant.id, "Day Ahead", "admin", true, null, System.nanoTime(), "Filter match");
    String html =
        mvc.perform(
                get("/admin/api-test-history")
                    .with(authentication(auth))
                    .param("filterPlant", String.valueOf(plant.id))
                    .param("filterApi", "Day Ahead")
                    .param("filterResult", "Failed"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains(
            "Apply filters",
            "From date (MYT)",
            "To date (MYT)",
            "No results match these filters",
            "value=\"Failed\" selected",
            "value=\"Day Ahead\" selected");
    var invalidDate =
        mvc.perform(
                get("/admin/api-test-history")
                    .with(authentication(auth))
                    .param("dateFrom", "invalid-date"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(invalidDate).contains("Choose valid dates", "role=\"alert\"");
    verifyNoInteractions(client);
  }

  @Test
  void historyPaginationPreservesAllFilters() throws Exception {
    for (int i = 0; i < 26; i++) {
      testHistory.save(
          plant.id, "Filter API " + i, "admin", false, null, System.nanoTime(), "Filtered " + i);
    }
    jdbc.update(
        "UPDATE \"ApiTestHistory\" SET \"TestedAt\"=? WHERE \"PlantId\"=?",
        java.time.OffsetDateTime.parse("2040-01-01T01:00:00Z"),
        plant.id);
    String html =
        mvc.perform(
                get("/admin/api-test-history")
                    .with(authentication(auth))
                    .param("filterPlant", String.valueOf(plant.id))
                    .param("filterResult", "Failed")
                    .param("dateFrom", "2040-01-01")
                    .param("dateTo", "2040-01-01"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains(
            "26 API result(s) found.",
            "filterPlant=" + plant.id,
            "filterResult=Failed",
            "dateFrom=2040-01-01",
            "page=1");
    String second =
        mvc.perform(
                get("/admin/api-test-history")
                    .with(authentication(auth))
                    .param("filterPlant", String.valueOf(plant.id))
                    .param("filterResult", "Failed")
                    .param("dateFrom", "2040-01-01")
                    .param("dateTo", "2040-01-01")
                    .param("page", "1"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(second).contains("Filtered 0", "Previous", "Page 2").doesNotContain("Filtered 25");
    verifyNoInteractions(client);
  }

  @Test
  void analyticsInvalidRangeShowsPopupAndKeepsFiltersOnBothPages() throws Exception {
    for (String path : java.util.List.of("/ddq-analytics", "/mmf-analytics")) {
      for (String end : java.util.List.of("2026-10-28T00:00", "2026-10-29T00:00")) {
        String html =
            mvc.perform(
                    get(path)
                        .with(authentication(auth))
                        .param("from", "2026-10-29T00:00")
                        .param("to", end)
                        .param("submission", "2026-10-28T09:00")
                        .param("dataType", "Week Ahead"))
                .andExpect(status().isOk())
                .andReturn()
                .getResponse()
                .getContentAsString();
        assertThat(html)
            .contains(
                "<dialog",
                "Check date and time",
                "Data until must be later than Data from",
                "method=\"dialog\"",
                ">OK</button>",
                "value=\"2026-10-29T00:00\"",
                "value=\"" + end + "\"",
                "value=\"2026-10-28T09:00\"")
            .doesNotContain("Request could not be completed");
      }
    }
  }

  @Test
  void analyticsMalformedDateShowsPopupAndValidRangeDoesNot() throws Exception {
    for (String field : java.util.List.of("from", "to", "submission")) {
      String html =
          mvc.perform(get("/ddq-analytics").with(authentication(auth)).param(field, "invalid"))
              .andExpect(status().isOk())
              .andReturn()
              .getResponse()
              .getContentAsString();
      assertThat(html).contains("analytics-filter-message", "Enter a valid date and time");
    }
    String valid =
        mvc.perform(
                get("/ddq-analytics")
                    .with(authentication(auth))
                    .param("from", "2026-10-28T00:00")
                    .param("to", "2026-10-29T00:00"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(valid).doesNotContain("analytics-filter-message");
  }
}
