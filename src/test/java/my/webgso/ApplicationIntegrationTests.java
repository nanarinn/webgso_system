package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
import static org.springframework.security.test.web.servlet.response.SecurityMockMvcResultMatchers.unauthenticated;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import java.io.*;
import java.math.*;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.apache.poi.xssf.usermodel.XSSFWorkbook;
import org.junit.jupiter.api.*;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.Authentication;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

@SpringBootTest
@AutoConfigureMockMvc
@ActiveProfiles("test")
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
class ApplicationIntegrationTests {
  @Autowired MockMvc mvc;
  @Autowired Store db;
  @Autowired Accounts accounts;
  @Autowired GoogleAccounts googleAccounts;
  @Autowired EmailVerification emailVerification;
  @Autowired CollectionService collection;
  @Autowired ReportService reports;
  @Autowired ApiTestHistoryService overviewHistory;
  @MockitoBean PlantApiClient client;
  @MockitoBean ManualBatchService batches;
  ApplicationUser admin, otherAdmin;
  Authentication adminAuth, otherAdminAuth;

  @BeforeAll
  void users() {
    admin = accounts.register("test.admin", "admin@gmail.com", "Test Admin", "StrongPassword123!");
    admin.accessStatus = "Approved";
    db.update(admin);
    accounts.setRole(admin.id, "Admin");
    adminAuth = accounts.token(admin);
    db.jdbc()
        .update("INSERT INTO \"GsoAccessReviewer\" (\"Id\",\"UserId\") VALUES (1,?)", admin.id);
    otherAdmin =
        accounts.register(
            "test.otherAdmin", "otherAdmin@gmail.com", "Other Admin", "StrongPassword123!");
    otherAdmin.accessStatus = "Approved";
    db.update(otherAdmin);
    accounts.setRole(otherAdmin.id, "Admin");
    otherAdminAuth = accounts.token(otherAdmin);
  }

  @BeforeEach
  void resetApi() {
    reset(client);
  }

  private void readableApi(String url) {
    when(client.fetch(url))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "[{\"timestamp\":\"2026-10-28T00:00:00\",\"value\":1.5,\"unit\":\"MW\"}]"));
  }

  private LssPlant apiSavePlant(String name) {
    var plant = new LssPlant();
    plant.plantId = "PROBE-" + System.nanoTime() % 1000000;
    plant.name = name;
    plant.capacityMw = BigDecimal.TEN;
    return db.insert(plant);
  }

  @Test
  void publicAccountsOnlyAcceptGmailAddresses() throws Exception {
    for (String email :
        List.of(
            "someone@outlook.com",
            "someone@gmail.com.evil.test",
            "someone@company.test",
            "someone@googlemail.com",
            "someone@sub.gmail.com")) {
      mvc.perform(
              post("/account/register")
                  .with(csrf())
                  .param("username", "gmail.policy")
                  .param("email", email)
                  .param("displayName", "Gmail policy")
                  .param("password", "StrongPassword123!")
                  .param("confirmPassword", "StrongPassword123!"))
          .andExpect(status().isBadRequest())
          .andExpect(content().string(org.hamcrest.Matchers.containsString(GmailAccounts.MESSAGE)));
      assertThatThrownBy(() -> googleAccounts.resolve("blockedSubject", email, true, "Blocked"))
          .isInstanceOf(IllegalArgumentException.class)
          .hasMessage(GmailAccounts.MESSAGE);
      mvc.perform(
              post("/account/login")
                  .with(csrf())
                  .param("identifier", email)
                  .param("password", "StrongPassword123!"))
          .andExpect(redirectedUrl("/login?error=true"));
    }
    assertThat(GmailAccounts.allowed("User.Name+tag@GMAIL.COM")).isTrue();
    assertThat(GmailAccounts.allowed("user@@gmail.com")).isFalse();
    assertThat(GmailAccounts.allowed("user@gmail.com ")).isFalse();
    assertThat(GmailAccounts.allowed(null)).isFalse();
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", admin.userName)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?error=true"));
    var legacy =
        accounts.register("legacy.mail", "legacy@outlook.com", "Legacy", "StrongPassword123!");
    legacy.accessStatus = "Approved";
    db.update(legacy);
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", legacy.email)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?error=true"));
    assertThat(db.list(ApplicationUser.class, "WHERE \"UserName\"=?", "gmail.policy")).isEmpty();
  }

  @Test
  void plantZoneAndStateAreSavedDisplayedAndRetainedWhenAnApiDraftFails() throws Exception {
    var plant = apiSavePlant("Zone state plant");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("plantId", "80301")
                .param("name", plant.name)
                .param("zone", "GSO North")
                .param("state", "Perak")
                .param("capacityMw", "30"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var saved = db.find(LssPlant.class, plant.id).orElseThrow();
    assertThat(saved.zone).isEqualTo("GSO North");
    assertThat(saved.state).isEqualTo("Perak");
    mvc.perform(get("/admin/lss-plants").param("q", plant.name).with(authentication(adminAuth)))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("GSO North")));
    mvc.perform(
            get("/admin/lss-plants/edit")
                .param("id", plant.id.toString())
                .with(authentication(adminAuth)))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("name=\"zone\"")))
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("<select name=\"state\"")));
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("saveApi", "apiDayAhead")
                .param("apiDayAhead", "not-a-url")
                .param("zone", "Draft zone")
                .param("state", "Selangor"))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("value=\"Draft zone\"")));
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().zone).isEqualTo("GSO North");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("plantId", "80301")
                .param("name", plant.name)
                .param("zone", "x".repeat(61))
                .param("state", "Perak")
                .param("capacityMw", "30"))
        .andExpect(status().isBadRequest());
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().zone).isEqualTo("GSO North");
  }

  @Test
  void cardSaveTestsAndPersistsOnlyTheChosenApiWithoutLosingDrafts() throws Exception {
    var plant = apiSavePlant("Card save target");
    readableApi("https://example.test/card/dayahead");
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("saveApi", "apiDayAhead")
                .param("name", "Unsaved plant name")
                .param("apiDayAhead", "https://example.test/card/dayahead")
                .param("apiWeekAhead", "https://example.test/unsaved/weekahead"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("API saved.")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString("https://example.test/unsaved/weekahead")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Test &amp; Save API")));
    var links = db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", plant.id);
    assertThat(links).hasSize(1);
    assertThat(links.getFirst().dataType).isEqualTo("Day Ahead");
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().name).isEqualTo("Card save target");
    verify(client).fetch("https://example.test/card/dayahead");
    verify(client, never()).fetch("https://example.test/unsaved/weekahead");
  }

  @Test
  void awaitingBrokerMmfShowsSpecificCauseForNewAndExistingPlants() throws Exception {
    String url = "https://example.test/bidor/live/mmf";
    when(client.fetch(url))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "{\"data\":{\"Weather_Station_SAE\":{},\"Weather_Station_SAE_1\":{}},\"data_status\":\"awaiting_broker\",\"data_type\":\"MMF\",\"last_received_at\":null}"));
    long before = db.count(LssPlant.class);
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("name", "Waiting MMF plant")
                .param("capacityMw", "30")
                .param("saveApi", "apiMmf")
                .param("apiMmf", url))
        .andExpect(status().isOk())
        .andExpect(view().name("edit"))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("awaiting_broker")))
        .andExpect(
            content()
                .string(org.hamcrest.Matchers.containsString("MQTT broker and data publisher")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("One or more API tests failed"))));
    assertThat(db.count(LssPlant.class)).isEqualTo(before);
    var existing = apiSavePlant("Waiting MMF existing plant");
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", existing.id.toString())
                .param("saveApi", "apiMmf")
                .param("apiMmf", url))
        .andExpect(status().isOk())
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString("API server is reachable (HTTP 200)")));
    assertThat(db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", existing.id)).isEmpty();
  }

  @Test
  void failedCardReplacementKeepsOriginalAndDoesNotImportAnyData() throws Exception {
    var plant = apiSavePlant("Failed replacement target");
    var link = new PlantApiConfiguration();
    link.plantId = plant.id;
    link.dataType = "Day Ahead";
    link.apiLink = "https://example.test/old";
    db.insert(link);
    long jobsBefore = db.count(CollectionJobEntity.class);
    for (var response :
        List.of(
            new PlantApiClient.Response(502, "application/json", "{}"),
            new PlantApiClient.Response(200, "application/json", "[]"),
            new PlantApiClient.Response(200, "text/html", "<html>Login</html>"))) {
      when(client.fetch("https://example.test/failing")).thenReturn(response);
      mvc.perform(
              post("/admin/lss-plants/save-api")
                  .with(authentication(adminAuth))
                  .with(csrf())
                  .param("id", plant.id.toString())
                  .param("saveApi", "apiDayAhead")
                  .param("apiDayAhead", "https://example.test/failing"))
          .andExpect(status().isOk())
          .andExpect(content().string(org.hamcrest.Matchers.containsString("API not saved.")))
          .andExpect(content().string(org.hamcrest.Matchers.containsString("Not saved")));
      assertThat(db.find(PlantApiConfiguration.class, link.id).orElseThrow().apiLink)
          .isEqualTo("https://example.test/old");
    }
    assertThat(db.count(CollectionJobEntity.class)).isEqualTo(jobsBefore);
  }

  @Test
  void savingPlantShowsEveryFailedApiUnderItsOwnButtonWithoutSavingChanges() throws Exception {
    var plant = apiSavePlant("Multiple failed APIs");
    when(client.fetch("https://example.test/fail-day"))
        .thenThrow(new IllegalArgumentException("Day API is offline"));
    when(client.fetch("https://example.test/fail-week"))
        .thenThrow(new IllegalArgumentException("Week API is offline"));
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("plantId", plant.plantId)
                .param("name", "Do not overwrite name")
                .param("capacityMw", "10")
                .param("apiDayAhead", "https://example.test/fail-day")
                .param("apiWeekAhead", "https://example.test/fail-week"))
        .andExpect(status().isBadRequest())
        .andExpect(view().name("edit"))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString(
                        "API test failed. API not saved. Day API is offline")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString(
                        "API test failed. API not saved. Week API is offline")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("api-save-failed")));
    verify(client).fetch("https://example.test/fail-day");
    verify(client).fetch("https://example.test/fail-week");
    assertThat(db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", plant.id)).isEmpty();
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().name)
        .isEqualTo("Multiple failed APIs");
  }

  @Test
  void availableAnalyticsDatesAndCollectionLinksUseSavedReadings() throws Exception {
    var plant = apiSavePlant("Available dates plant");
    for (String type : List.of("Day Ahead", "MMF")) {
      var job = new CollectionJobEntity();
      job.collectionId = "AVAILABLE-" + UUID.randomUUID();
      job.plantId = plant.id;
      job.dataType = type;
      job.status = "Completed";
      job.submissionDateTime = LocalDateTime.of(2032, 10, 29, 9, 0);
      db.insert(job);
      for (var time :
          List.of(LocalDateTime.of(2032, 10, 31, 0, 0), LocalDateTime.of(2032, 11, 8, 23, 45))) {
        var r = new SolarApiReading();
        r.plantId = plant.id;
        r.collectionJobId = job.id;
        r.dataType = type;
        r.parameter = type.equals("MMF") ? "Humidity" : "ForecastValue";
        r.value = BigDecimal.ONE;
        r.intervalStart = time;
        r.intervalEnd = time.plusMinutes(15);
        r.submissionDateTime = job.submissionDateTime;
        db.insert(r);
      }
      String route = type.equals("MMF") ? "/mmf-analytics" : "/ddq-analytics";
      mvc.perform(
              get(route)
                  .with(authentication(adminAuth))
                  .param("plantIds", plant.id.toString())
                  .param("dataType", type)
                  .param("parameter", type.equals("MMF") ? "Humidity" : "")
                  .param("from", "2032-10-29T00:00")
                  .param("to", "2032-10-30T00:00"))
          .andExpect(status().isOk())
          .andExpect(model().attribute("total", 0))
          .andExpect(
              content()
                  .string(org.hamcrest.Matchers.containsString("Data available: 31 Oct 2032")));
      mvc.perform(
              get(route)
                  .with(authentication(adminAuth))
                  .param("plantIds", plant.id.toString())
                  .param("dataType", type)
                  .param("parameter", type.equals("MMF") ? "Humidity" : "")
                  .param("available", "true")
                  .param("from", "invalid-old-date"))
          .andExpect(status().isOk())
          .andExpect(model().attribute("total", 2))
          .andExpect(model().attribute("from", LocalDateTime.of(2032, 10, 31, 0, 0)))
          .andExpect(model().attribute("to", LocalDateTime.of(2032, 11, 9, 0, 0)));
      var redirect =
          mvc.perform(get("/collections/" + job.id + "/analytics").with(authentication(adminAuth)))
              .andExpect(status().is3xxRedirection())
              .andReturn()
              .getResponse()
              .getRedirectedUrl();
      assertThat(redirect).startsWith(route + "?");
      var redirectedRequest = get(route).with(authentication(adminAuth));
      org.springframework.web.util.UriComponentsBuilder.fromUriString(redirect)
          .build()
          .getQueryParams()
          .forEach(
              (key, values) ->
                  values.forEach(
                      value ->
                          redirectedRequest.param(
                              key,
                              java.net.URLDecoder.decode(
                                  value, java.nio.charset.StandardCharsets.UTF_8))));
      mvc.perform(redirectedRequest)
          .andExpect(status().isOk())
          .andExpect(model().attribute("total", 2));
      mvc.perform(get("/collections/" + job.id).with(authentication(adminAuth)))
          .andExpect(content().string(org.hamcrest.Matchers.containsString("View analytics")));
      mvc.perform(
              get(route)
                  .with(authentication(adminAuth))
                  .param("plantIds", plant.id.toString())
                  .param("dataType", type)
                  .param("parameter", type.equals("MMF") ? "Humidity" : "")
                  .param("from", "2032-10-31T00:00")
                  .param("to", "2032-11-09T00:00")
                  .param("downloadCsv", "true"))
          .andExpect(status().isOk())
          .andExpect(
              header()
                  .string(
                      "Content-Disposition",
                      org.hamcrest.Matchers.containsString(
                          type.equals("MMF") ? "gso-mmf-analytics.csv" : "gso-ddq-analytics.csv")))
          .andExpect(
              content()
                  .string(
                      org.hamcrest.Matchers.containsString(
                          type.equals("MMF") ? "Humidity" : "ForecastValue")));
    }
    var empty = apiSavePlant("No available readings");
    mvc.perform(
            get("/ddq-analytics")
                .with(authentication(adminAuth))
                .param("plantIds", empty.id.toString())
                .param("available", "true"))
        .andExpect(status().isOk())
        .andExpect(model().attribute("total", 0))
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("No saved data is available")));
  }

  @Test
  void analyticsCsvIncludesEveryFilteredPageWithoutOtherPlants() throws Exception {
    var plant = apiSavePlant("CSV readings plant");
    var job = new CollectionJobEntity();
    job.collectionId = "CSV-READINGS-" + UUID.randomUUID();
    job.plantId = plant.id;
    job.dataType = "Day Ahead";
    db.insert(job);
    for (int i = 0; i < 26; i++) {
      var r = new SolarApiReading();
      r.plantId = plant.id;
      r.collectionJobId = job.id;
      r.dataType = "Day Ahead";
      r.parameter = "ForecastValue";
      r.value = BigDecimal.valueOf(i);
      r.intervalStart = LocalDateTime.of(2034, 10, 1, 0, 0).plusMinutes(i * 15L);
      r.intervalEnd = r.intervalStart.plusMinutes(15);
      db.insert(r);
    }
    var bytes =
        mvc.perform(
                get("/ddq-analytics")
                    .with(authentication(adminAuth))
                    .param("plantIds", plant.id.toString())
                    .param("dataType", "Day Ahead")
                    .param("from", "2034-10-01T00:00")
                    .param("to", "2034-10-02T00:00")
                    .param("page", "1")
                    .param("downloadCsv", "true"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsByteArray();
    String csv = new String(bytes, java.nio.charset.StandardCharsets.UTF_8);
    try (var parsed =
        org.apache.commons.csv.CSVParser.parse(
            csv.substring(1),
            org.apache.commons.csv.CSVFormat.DEFAULT
                .builder()
                .setHeader()
                .setSkipHeaderRecord(true)
                .get())) {
      assertThat(parsed.getRecords())
          .hasSize(26)
          .allSatisfy(row -> assertThat(row.get("Plant")).isEqualTo(plant.plantId));
    }
    mvc.perform(get("/ddq-analytics").param("downloadCsv", "true"))
        .andExpect(status().is3xxRedirection());
  }

  @Test
  void databaseStatusAppearsBetweenGsoAndPartnerLogo() throws Exception {
    String html =
        mvc.perform(get("/account/profile").with(authentication(adminAuth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html).contains("Database connected", "sidebar-database-status");
    assertThat(html.indexOf("sidebar-status")).isLessThan(html.indexOf("sidebar-database-status"));
    assertThat(html.indexOf("sidebar-database-status"))
        .isLessThan(html.indexOf("system-footer system-footer-sidebar"));
  }

  @Test
  void duplicatePlantNamesAreRejectedOnCreateAndRenameButOwnNameCanBeSaved() throws Exception {
    var existing = apiSavePlant("BDNS Duplicate Check");
    long before = db.count(LssPlant.class);
    for (String name :
        List.of(
            "BDNS Duplicate Check",
            "bdns duplicate check",
            " BDNS   Duplicate\tCheck ",
            "BDNS\u00a0Duplicate Check")) {
      mvc.perform(
              post("/admin/lss-plants/save")
                  .with(authentication(adminAuth))
                  .with(csrf())
                  .param("name", name)
                  .param("capacityMw", "30"))
          .andExpect(status().isBadRequest())
          .andExpect(view().name("edit"))
          .andExpect(
              content()
                  .string(org.hamcrest.Matchers.containsString("This plant name already exists")));
    }
    assertThat(db.count(LssPlant.class)).isEqualTo(before);
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", existing.id.toString())
                .param("plantId", existing.plantId)
                .param("name", existing.name)
                .param("capacityMw", "40"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var other = apiSavePlant("KMPR Duplicate Check");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", other.id.toString())
                .param("plantId", other.plantId)
                .param("name", "bdns duplicate check")
                .param("capacityMw", "30"))
        .andExpect(status().isBadRequest());
    assertThat(db.find(LssPlant.class, other.id).orElseThrow().name)
        .isEqualTo("KMPR Duplicate Check");
  }

  @Test
  void newPlantsReceiveNextNumericIdsAndApiCardKeepsAssignedId() throws Exception {
    var maximum =
        db.list(LssPlant.class, "").stream()
            .map(p -> p.plantId)
            .filter(value -> value.matches("[0-9]{1,20}"))
            .map(java.math.BigInteger::new)
            .max(java.math.BigInteger::compareTo)
            .orElse(java.math.BigInteger.ZERO);
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "")
                .param("name", "AUTO BDNS")
                .param("capacityMw", "30"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var saved = db.list(LssPlant.class, "WHERE \"Name\"=?", "AUTO BDNS").getFirst();
    assertThat(new java.math.BigInteger(saved.plantId))
        .isEqualTo(maximum.add(java.math.BigInteger.ONE));
    assertThat(saved.plantId).matches("[0-9]{2,20}");
    readableApi("https://example.test/auto/dayahead");
    var result =
        mvc.perform(
                post("/admin/lss-plants/save-api")
                    .with(authentication(adminAuth))
                    .with(csrf())
                    .param("plantId", "")
                    .param("name", "AUTO KMPR")
                    .param("capacityMw", "30")
                    .param("saveApi", "apiDayAhead")
                    .param("apiDayAhead", "https://example.test/auto/dayahead"))
            .andExpect(status().isOk())
            .andExpect(view().name("edit"))
            .andReturn();
    var apiPlant = (LssPlant) result.getModelAndView().getModel().get("plant");
    assertThat(new java.math.BigInteger(apiPlant.plantId))
        .isEqualTo(maximum.add(java.math.BigInteger.TWO));
    assertThat(db.find(LssPlant.class, apiPlant.id).orElseThrow().plantId)
        .isEqualTo(apiPlant.plantId);
    mvc.perform(get("/admin/lss-plants/edit").with(authentication(adminAuth)))
        .andExpect(
            content()
                .string(org.hamcrest.Matchers.containsString("Assigned automatically when saved")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("readonly=\"readonly\"")));
  }

  @Test
  void plantIdAcceptsOnlyDigitsAndPreservesLeadingZeros() throws Exception {
    long before = db.count(LssPlant.class);
    for (String invalid : List.of("BDNS", "1.5", "-1", "1e3")) {
      mvc.perform(
              post("/admin/lss-plants/save")
                  .with(authentication(adminAuth))
                  .with(csrf())
                  .param("plantId", invalid)
                  .param("name", "KMPR")
                  .param("capacityMw", "10"))
          .andExpect(status().isBadRequest())
          .andExpect(view().name("edit"))
          .andExpect(
              content()
                  .string(
                      org.hamcrest.Matchers.containsString("Plant ID must contain numbers only")));
    }
    assertThat(db.count(LssPlant.class)).isEqualTo(before);
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "0093001")
                .param("name", "KMPR")
                .param("capacityMw", "10"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var saved = db.list(LssPlant.class, "WHERE \"PlantId\"=?", "0093001").getFirst();
    assertThat(saved.name).isEqualTo("KMPR");
    mvc.perform(
            get("/admin/lss-plants/edit")
                .param("id", saved.id.toString())
                .with(authentication(adminAuth)))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("pattern=\"[0-9]+\"")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("e.g. BDNS or KMPR")));
  }

  @Test
  void generalApiFailureStaysOnPlantFormWithDismissiblePopup() throws Exception {
    var plant = apiSavePlant("Popup original plant");
    when(client.fetch("https://example.test/popup-offline"))
        .thenThrow(
            new IllegalArgumentException(
                "Could not connect to the API server. Check the host and port."));
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("plantId", plant.plantId)
                .param("name", "Unsaved popup draft")
                .param("capacityMw", "10")
                .param("ddqApiEndpoint", "https://example.test/popup-offline"))
        .andExpect(status().isBadRequest())
        .andExpect(view().name("edit"))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("request-error-title")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString("Could not connect to the API server")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Unsaved popup draft")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("GSO Web Services")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Return to repository"))));
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().name)
        .isEqualTo("Popup original plant");
  }

  @Test
  void failedNewApiCannotBeSavedThroughEitherFormOrGenericApiPage() throws Exception {
    var plant = apiSavePlant("Blocked endpoints");
    when(client.fetch("https://example.test/offline"))
        .thenThrow(new IllegalArgumentException("API connection failed"));
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("saveApi", "apiMonth4")
                .param("apiMonth4", "https://example.test/offline"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("API not saved.")));
    mvc.perform(
            post("/admin/api-configurations/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", plant.id.toString())
                .param("apiType", "DDQ")
                .param("dataType", "Day Ahead")
                .param("apiLink", "https://example.test/offline"))
        .andExpect(status().isBadRequest());
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "FAIL-NEW-API")
                .param("name", "Do not create")
                .param("capacityMw", "10")
                .param("apiDayAhead", "https://example.test/offline"))
        .andExpect(status().isBadRequest());
    assertThat(db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", plant.id)).isEmpty();
    assertThat(db.list(LssPlant.class, "WHERE \"PlantId\"=?", "FAIL-NEW-API")).isEmpty();
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .param("id", plant.id.toString())
                .param("saveApi", "apiMonth4")
                .param("apiMonth4", "https://example.test/offline"))
        .andExpect(status().isForbidden());
  }

  @Test
  void newPlantCardTestsBeforeCreationAndKeepsOtherApisUnsaved() throws Exception {
    readableApi("https://example.test/new/month4");
    mvc.perform(
            post("/admin/lss-plants/save-api")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("saveApi", "apiMonth4")
                .param("plantId", "900001")
                .param("name", "New card plant")
                .param("capacityMw", "10")
                .param("apiMonth4", "https://example.test/new/month4")
                .param("apiMonth1", "https://example.test/not-yet-saved"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Plant and API saved.")));
    var plant = db.list(LssPlant.class, "WHERE \"PlantId\"=?", "900001").getFirst();
    assertThat(db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", plant.id)).hasSize(1);
    verify(client, never()).fetch("https://example.test/not-yet-saved");
  }

  @Test
  void authenticationAndEveryPageRenderWithCorrectAccess() throws Exception {
    mvc.perform(get("/")).andExpect(status().is3xxRedirection());
    mvc.perform(get("/api/status")).andExpect(status().isUnauthorized());
    mvc.perform(get("/health")).andExpect(status().isOk());
    mvc.perform(get("/login"))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("Sign in to your workspace")));
    mvc.perform(get("/register")).andExpect(status().isOk());
    for (String path :
        List.of(
            "/",
            "/ddq-analytics",
            "/mmf-analytics",
            "/collection-management",
            "/on-demand-analysis",
            "/api/status",
            "/api/dashboard"))
      mvc.perform(get(path).with(authentication(adminAuth))).andExpect(status().isOk());
    for (var resource : Resources.ALL) {
      mvc.perform(get(resource.path()).with(authentication(adminAuth))).andExpect(status().isOk());
      if (!resource.editable().isEmpty() && resource.type() != DataAnomaly.class)
        mvc.perform(get(resource.path() + "/edit").with(authentication(adminAuth)))
            .andExpect(status().isOk());
      mvc.perform(get(resource.path()).with(authentication(otherAdminAuth)))
          .andExpect(status().isOk());
    }
    mvc.perform(post("/collections/run").with(authentication(otherAdminAuth)))
        .andExpect(status().isForbidden());
  }

  @Test
  @org.springframework.transaction.annotation.Transactional
  void adminOverviewUsesSavedTestResultsForEveryAdmin() throws Exception {
    var passed = new LssPlant();
    passed.plantId = "OVERVIEW-PASS";
    passed.name = "Overview passed API";
    passed.apiConnectionStatus = "Connected";
    passed.lastApiTestAt = OffsetDateTime.parse("2026-10-21T23:15:00Z");
    db.insert(passed);
    var failed = new LssPlant();
    failed.plantId = "OVERVIEW-FAIL";
    failed.name = "Overview failed API";
    failed.apiConnectionStatus = "Failed";
    db.insert(failed);
    passed.mmfApiEndpoint = "https://example.test/mmf";
    failed.mmfApiEndpoint = "https://example.test/mmf";
    db.update(passed);
    db.update(failed);
    overviewHistory.save(passed.id, "MMF", "admin", true, null, System.nanoTime(), "Passed");
    overviewHistory.save(failed.id, "MMF", "admin", false, null, System.nanoTime(), "Failed");
    mvc.perform(get("/").with(authentication(adminAuth)))
        .andExpect(status().isOk())
        .andExpect(view().name("admin-overview"))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Total APIs")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Not tested")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Admin accounts"))));
    var filtered =
        mvc.perform(get("/").param("apiStatus", "failed").with(authentication(adminAuth)))
            .andExpect(status().isOk())
            .andReturn();
    @SuppressWarnings("unchecked")
    var activity =
        (List<Map<String, Object>>) filtered.getModelAndView().getModel().get("apiActivity");
    assertThat(activity)
        .isNotEmpty()
        .allSatisfy(row -> assertThat(row.get("tone")).isEqualTo("failed"));
    mvc.perform(get("/").with(authentication(otherAdminAuth)))
        .andExpect(status().isOk())
        .andExpect(view().name("admin-overview"))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Admin accounts"))));
  }

  @Test
  void javaBatchStatusRefreshesOnlyItsFrameAndRetainsOwnerCheck() throws Exception {
    var batch = new ManualBatchService.Batch("user:" + admin.id);
    batch.items.put(1, new ManualBatchService.Item(1, "Queued plant", "Queued", "Waiting", null));
    when(batches.get(batch.id, "user:" + admin.id)).thenReturn(batch);
    when(batches.get(batch.id, "user:" + otherAdmin.id))
        .thenThrow(
            new org.springframework.security.access.AccessDeniedException(
                "This batch belongs to another user"));
    mvc.perform(get("/collections/batches/" + batch.id + "/status").with(authentication(adminAuth)))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("http-equiv=\"refresh\"")));
    mvc.perform(
            get("/collection-management").param("batch", batch.id).with(authentication(adminAuth)))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("<iframe")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("http-equiv=\"refresh\""))));
    mvc.perform(
            get("/collections/batches/" + batch.id + "/status")
                .with(authentication(otherAdminAuth)))
        .andExpect(status().isForbidden());
    batch.items.put(1, new ManualBatchService.Item(1, "Queued plant", "Completed", "Saved", 1L));
    mvc.perform(get("/collections/batches/" + batch.id + "/status").with(authentication(adminAuth)))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Batch completed")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("http-equiv=\"refresh\""))));
  }

  @Test
  void javaRenderedLoginFormIncludesAUsableCsrfToken() throws Exception {
    var login = mvc.perform(get("/login")).andExpect(status().isOk()).andReturn();
    String html = login.getResponse().getContentAsString();
    assertThat(html).doesNotContain("th:", "<script", "java-ui.js");
    var token =
        java.util.regex.Pattern.compile("name=\"_csrf\"[^>]*value=\"([^\"]+)\"").matcher(html);
    assertThat(token.find()).isTrue();
    var session =
        (org.springframework.mock.web.MockHttpSession) login.getRequest().getSession(false);
    assertThat(session).isNotNull();
    mvc.perform(
            post("/account/login")
                .session(session)
                .param("identifier", admin.email)
                .param("password", "StrongPassword123!")
                .param("_csrf", token.group(1)))
        .andExpect(redirectedUrl("/"));
    mvc.perform(
            post("/account/login")
                .param("identifier", admin.email)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?reason=form-expired"))
        .andExpect(unauthenticated());
  }

  @Test
  void expiredLoginTokenReturnsToFreshFormButOtherPostsRemainProtected() throws Exception {
    var login = mvc.perform(get("/login")).andReturn();
    var token =
        java.util.regex.Pattern.compile("name=\"_csrf\"[^>]*value=\"([^\"]+)\"")
            .matcher(login.getResponse().getContentAsString());
    assertThat(token.find()).isTrue();
    // The token came from an old session; the next request deliberately has no session cookie.
    mvc.perform(
            post("/account/login")
                .param("_csrf", token.group(1))
                .param("identifier", admin.email)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?reason=form-expired"))
        .andExpect(unauthenticated());
    mvc.perform(get("/login?reason=form-expired"))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("Your sign-in form expired")));
    mvc.perform(post("/api/admin/plants/test")).andExpect(status().isForbidden());
    mvc.perform(post("/account/register")).andExpect(status().isForbidden());
  }

  @Test
  void visitingLoginProcessingAddressRedirectsToLoginPage() throws Exception {
    mvc.perform(get("/account/login")).andExpect(redirectedUrl("/login"));
  }

  @Test
  @org.springframework.transaction.annotation.Transactional
  void plantTableShowsEndpointsStatusAndPerPlantTestActions() throws Exception {
    var plant = new LssPlant();
    plant.plantId = "TABLE-API";
    plant.name = "Table plant";
    plant.ddqApiEndpoint = "https://example.test/ddq";
    plant.mmfApiEndpoint = "https://example.test/mmf";
    plant.isActive = true;
    db.insert(plant);
    var override = new PlantApiConfiguration();
    override.plantId = plant.id;
    override.apiType = "DDQ";
    override.dataType = "Day Ahead";
    override.apiLink = "https://example.test/day-ahead";
    db.insert(override);
    var inactive = new LssPlant();
    inactive.plantId = "TABLE-OFF";
    inactive.name = "Inactive table plant";
    inactive.isActive = false;
    db.insert(inactive);
    String html =
        mvc.perform(get("/admin/lss-plants").with(authentication(adminAuth)).param("q", "TABLE-"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html)
        .contains(
            "Plant Name",
            "Capacity (MW)",
            "https://example.test/ddq",
            "https://example.test/mmf",
            "https://example.test/day-ahead",
            "Not configured",
            "Test API",
            "action=\"/admin/plants/" + plant.id + "/test\"",
            "name=\"_csrf\"")
        .doesNotContain("Test a configured API", "API connection status", "Is active");
  }

  @Test
  void missingPlantTestReturnsPopupOnPlantList() throws Exception {
    var result =
        mvc.perform(
                post("/admin/plants/test")
                    .with(authentication(adminAuth))
                    .with(csrf())
                    .param("testPlantId", "2147483647")
                    .param("dataType", "Rolling 24 Hour"))
            .andExpect(redirectedUrl("/admin/lss-plants"))
            .andExpect(flash().attribute("apiTestTitle", "Plant not found"))
            .andReturn();
    mvc.perform(
            get("/admin/lss-plants")
                .with(authentication(adminAuth))
                .flashAttrs(result.getFlashMap()))
        .andExpect(status().isOk())
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.containsString("class=\"api-test-popup\" open=\"open\"")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("method=\"dialog\"")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Check the Plant ID")));
  }

  @Test
  void failedPlantTestReturnsPopupAndKeepsFailureStatus() throws Exception {
    var plant = new LssPlant();
    plant.plantId = "POPUP-FAIL";
    plant.name = "Popup test";
    plant.ddqApiEndpoint = "https://example.test/popup";
    db.insert(plant);
    when(client.fetch(plant.ddqApiEndpoint))
        .thenThrow(new IllegalArgumentException("API host could not be resolved."));
    mvc.perform(
            post("/admin/plants/" + plant.id + "/test")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("dataType", "Rolling 24 Hour"))
        .andExpect(redirectedUrl("/admin/lss-plants"))
        .andExpect(flash().attribute("apiTestTitle", "Connection test failed"))
        .andExpect(flash().attribute("apiTestMessage", "API host could not be resolved."));
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().apiConnectionStatus)
        .isEqualTo("Failed");
  }

  @Test
  void javaPlantTestFormWorksWithoutBrowserScript() throws Exception {
    var plant = new LssPlant();
    plant.plantId = "JAVA-UI-TEST";
    plant.name = "Java UI API test";
    plant.ddqApiEndpoint = "https://example.test/java-ui";
    db.insert(plant);
    when(client.fetch(plant.ddqApiEndpoint))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"2026-10-20T00:00:00\",\"power\":10}"));
    mvc.perform(
            post("/admin/plants/test")
                .with(authentication(otherAdminAuth))
                .with(csrf())
                .param("testPlantId", plant.id.toString())
                .param("dataType", "Rolling 24 Hour"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    mvc.perform(
            post("/admin/plants/test")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("testPlantId", plant.id.toString())
                .param("dataType", "Rolling 24 Hour"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    assertThat(db.find(LssPlant.class, plant.id).orElseThrow().apiConnectionStatus)
        .isEqualTo("Connected");
  }

  @Test
  void registrationRequiresOwnEmailVerificationWithoutAnotherUserApproval() throws Exception {
    String name = "verify." + System.nanoTime();
    String email = name + "@gmail.com";
    mvc.perform(get("/register"))
        .andExpect(status().isOk())
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("name=\"displayName\""))))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(org.hamcrest.Matchers.containsString("Full name"))));
    mvc.perform(
            post("/account/register")
                .with(csrf())
                .param("username", name)
                .param("email", email)
                .param("password", "StrongPassword123!")
                .param("confirmPassword", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?registered=success"));
    var user =
        db.list(
                ApplicationUser.class,
                "WHERE \"NormalizedEmail\"=?",
                email.toUpperCase(Locale.ROOT))
            .getFirst();
    assertThat(user.displayName).isEqualTo(name);
    assertThat(user.accessStatus).isEqualTo("Pending");
    assertThat(user.emailConfirmed).isFalse();
    assertThat(
            db.list(
                UserAccessNotification.class,
                "WHERE \"RelatedUserId\"=? AND \"NotificationType\"='AccessRequest'",
                user.id))
        .isEmpty();
    String token = verificationToken(user.id);
    assertThat(
            db.jdbc()
                .queryForObject(
                    "SELECT \"TokenHash\" FROM \"GsoEmailVerifications\" WHERE \"UserId\"=?",
                    String.class,
                    user.id))
        .hasSize(64)
        .isNotEqualTo(token);
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", email)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?error=true"));
    mvc.perform(get("/account/verify-email").param("token", token))
        .andExpect(status().isOk())
        .andExpect(header().string("Referrer-Policy", "no-referrer"));
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().accessStatus)
        .isEqualTo("Pending");
    mvc.perform(post("/account/verify-email").param("token", token))
        .andExpect(status().isForbidden());
    mvc.perform(post("/account/verify-email").with(csrf()).param("token", token))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Email verified")));
    var verified = db.find(ApplicationUser.class, user.id).orElseThrow();
    assertThat(verified.emailConfirmed).isTrue();
    assertThat(verified.accessStatus).isEqualTo("Approved");
    assertThat(verified.securityStamp).isNotEqualTo(user.securityStamp);
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", email)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/"));
    assertThat(emailVerification.verify(token)).isFalse();
    mvc.perform(get("/register"))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Admin review"))));
  }

  private String verificationToken(String userId) {
    var note =
        db.list(
                UserAccessNotification.class,
                "WHERE \"UserId\"=? AND \"NotificationType\"='EmailVerification' ORDER BY \"Id\""
                    + " DESC",
                userId)
            .getFirst();
    var matcher = java.util.regex.Pattern.compile("token=([A-Za-z0-9_-]{43})").matcher(note.body);
    assertThat(matcher.find()).isTrue();
    return matcher.group(1);
  }

  @Test
  void googleRegistrationActivatesOnlyVerifiedIdentitiesWithoutApproval() throws Exception {
    String subject = "google" + System.nanoTime();
    String email = subject + "@gmail.com";
    var user = googleAccounts.resolve(subject, email, true, "Google User");
    assertThat(user.accessStatus).isEqualTo("Approved");
    assertThat(user.passwordHash).isNull();
    assertThat(user.emailConfirmed).isTrue();
    assertThat(googleAccounts.resolve(subject, email, true, "Google User").id).isEqualTo(user.id);
    assertThat(db.list(UserAccessNotification.class, "WHERE \"RelatedUserId\"=?", user.id))
        .isEmpty();
    mvc.perform(get("/").with(authentication(accounts.token(user)))).andExpect(status().isOk());
    accounts.review(adminAuth, user.id, "Rejected", Accounts.ADMIN, "Access removed");
    assertThatThrownBy(() -> googleAccounts.resolve(subject, email, true, "Google User"))
        .isInstanceOf(org.springframework.security.authentication.DisabledException.class);
  }

  @Test
  void googleCannotLinkAnExistingEmailOrUseAnUnverifiedIdentity() {
    assertThatThrownBy(() -> googleAccounts.resolve("otherSubject", admin.email, true, "Other"))
        .isInstanceOf(GoogleAccounts.ExistingEmailException.class);
    assertThatThrownBy(
            () ->
                googleAccounts.resolve("unverifiedSubject", "unverified@gmail.com", false, "Other"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(
            db.jdbc()
                .queryForObject(
                    "SELECT COUNT(*) FROM \"GsoGoogleIdentities\" WHERE \"Subject\" IN"
                        + " ('otherSubject','unverifiedSubject')",
                    Long.class))
        .isZero();
  }

  @Test
  void retiredGoogleLoginDoesNotOfferGoogleButtonsOrSetupMessages() throws Exception {
    mvc.perform(get("/account/google")).andExpect(redirectedUrl("/login"));
    mvc.perform(get("/login").param("google", "unavailable"))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("not configured yet"))));
    mvc.perform(get("/register"))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Continue with Google"))));
  }

  @Test
  void verificationRejectsExpiredChangedEmailAndInactiveAccounts() {
    String suffix = "expiry." + System.nanoTime();
    var user =
        emailVerification.register(
            suffix, suffix + "@gmail.com", "Expiry user", "StrongPassword123!");
    String token = verificationToken(user.id);
    db.jdbc()
        .update(
            "UPDATE \"GsoEmailVerifications\" SET \"ExpiresAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now().minusMinutes(1),
            user.id);
    assertThat(emailVerification.verify(token)).isFalse();
    db.jdbc()
        .update(
            "UPDATE \"GsoEmailVerifications\" SET \"RequestedAt\"=? WHERE \"UserId\"=?",
            OffsetDateTime.now().minusMinutes(2),
            user.id);
    emailVerification.resend(user.email);
    String replacement = verificationToken(user.id);
    assertThat(replacement).isNotEqualTo(token);
    assertThat(emailVerification.verify(token)).isFalse();
    user.normalizedEmail = "CHANGED@EXAMPLE.TEST";
    db.update(user);
    assertThat(emailVerification.verify(replacement)).isFalse();
    user.normalizedEmail = user.email.toUpperCase(Locale.ROOT);
    user.accessStatus = "Inactive";
    db.update(user);
    assertThat(emailVerification.verify(replacement)).isFalse();
    assertThat(emailVerification.verify("forged-token")).isFalse();
    assertThat(db.find(ApplicationUser.class, user.id).orElseThrow().accessStatus)
        .isEqualTo("Inactive");
  }

  @Test
  void rejectedRegistrationCannotAuthenticateAndPendingSessionCannotReview() throws Exception {
    String name = "pending." + System.nanoTime();
    var target = accounts.register(name, name + "@gmail.com", "Pending user", "StrongPassword123!");
    mvc.perform(
            post("/admin/users/" + otherAdmin.id + "/review")
                .with(authentication(accounts.token(target)))
                .with(csrf())
                .param("decision", "Approved"))
        .andExpect(status().is3xxRedirection());
    accounts.review(adminAuth, target.id, "Rejected", Accounts.ADMIN, "Access not approved");
    assertThatThrownBy(
            () ->
                accounts.authenticate(
                    org.springframework.security.authentication.UsernamePasswordAuthenticationToken
                        .unauthenticated(target.email, "StrongPassword123!")))
        .isInstanceOf(org.springframework.security.authentication.DisabledException.class);
    assertThat(db.find(ApplicationUser.class, admin.id).orElseThrow().accessStatus)
        .isEqualTo("Approved");
  }

  @Test
  void approvalRoutesAreRetiredAndResendIsGenericAndRateLimited() throws Exception {
    for (var actor : List.of(adminAuth, otherAdminAuth)) {
      mvc.perform(get("/").with(authentication(actor)))
          .andExpect(
              content()
                  .string(
                      org.hamcrest.Matchers.not(
                          org.hamcrest.Matchers.containsString("href=\"/admin/users\""))));
      mvc.perform(get("/admin/users").with(authentication(actor)))
          .andExpect(status().isForbidden());
      mvc.perform(
              post("/admin/users/anything/review")
                  .with(authentication(actor))
                  .with(csrf())
                  .param("decision", "Approved"))
          .andExpect(status().isForbidden());
    }
    String suffix = "resend." + System.nanoTime();
    var user =
        emailVerification.register(
            suffix, suffix + "@gmail.com", "Resend user", "StrongPassword123!");
    String first = verificationToken(user.id);
    for (String email : List.of(user.email, "unknown@gmail.com", admin.email)) {
      mvc.perform(post("/account/resend-verification").with(csrf()).param("email", email))
          .andExpect(redirectedUrl("/login?verification=sent"));
    }
    assertThat(verificationToken(user.id)).isEqualTo(first);
    assertThat(
            db.list(
                UserAccessNotification.class,
                "WHERE \"UserId\"=? AND \"NotificationType\"='EmailVerification'",
                user.id))
        .hasSize(1);
  }

  @Test
  @org.springframework.transaction.annotation.Transactional
  void existingRoleAssignmentsAreReadAsAdminWithoutRewritingAccounts() {
    for (String previous : List.of("Staff", "Administrator", "SuperAdministrator")) {
      var legacy =
          accounts.register(
              "legacy." + previous,
              previous + "@gmail.com",
              "Existing Admin",
              "StrongPassword123!");
      db.jdbc().update("DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\"=?", legacy.id);
      legacy.accessStatus = "Approved";
      db.update(legacy);
      String roleId = UUID.randomUUID().toString();
      db.jdbc()
          .update(
              "INSERT INTO \"AspNetRoles\""
                  + " (\"Id\",\"Name\",\"NormalizedName\",\"ConcurrencyStamp\") VALUES (?,?,?,?)",
              roleId,
              previous,
              previous.toUpperCase(Locale.ROOT),
              UUID.randomUUID().toString());
      db.jdbc()
          .update(
              "INSERT INTO \"AspNetUserRoles\" (\"UserId\",\"RoleId\") VALUES (?,?)",
              legacy.id,
              roleId);
      assertThat(accounts.roles(legacy.id)).containsExactly(Accounts.ADMIN);
      assertThat(accounts.token(legacy).getAuthorities())
          .extracting("authority")
          .containsExactly("ROLE_Admin");
      assertThat(
              db.jdbc()
                  .queryForObject(
                      "SELECT \"RoleId\" FROM \"AspNetUserRoles\" WHERE \"UserId\"=?",
                      String.class,
                      legacy.id))
          .isEqualTo(roleId);
      assertThatThrownBy(() -> accounts.setRole(legacy.id, previous))
          .isInstanceOf(IllegalArgumentException.class);
    }
  }

  @Test
  void approvedAccountWithoutAdminAssignmentCannotAccessProtectedPages() throws Exception {
    var unassigned =
        accounts.register(
            "unassigned.admin", "unassigned@gmail.com", "Unassigned", "StrongPassword123!");
    db.jdbc().update("DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\"=?", unassigned.id);
    unassigned.accessStatus = "Approved";
    db.update(unassigned);
    var auth = accounts.token(unassigned);
    mvc.perform(get("/").with(authentication(auth))).andExpect(status().isForbidden());
    mvc.perform(get("/admin/users").with(authentication(auth))).andExpect(status().isForbidden());
    assertThatThrownBy(
            () -> accounts.review(auth, otherAdmin.id, "Inactive", Accounts.ADMIN, "Denied"))
        .isInstanceOf(org.springframework.security.access.AccessDeniedException.class);
  }

  @Test
  void failedSignInsLockAccountAndCorrectPasswordDoesNotBypassLockout() {
    var target =
        accounts.register(
            "lock." + System.nanoTime(),
            "lock." + System.nanoTime() + "@gmail.com",
            "Lock Test",
            "StrongPassword123!");
    target.accessStatus = "Approved";
    db.update(target);
    for (int i = 0; i < 5; i++)
      assertThatThrownBy(
              () ->
                  accounts.authenticate(
                      UsernamePasswordAuthenticationToken.unauthenticated(target.email, "wrong")))
          .isInstanceOf(org.springframework.security.authentication.BadCredentialsException.class);
    assertThatThrownBy(
            () ->
                accounts.authenticate(
                    UsernamePasswordAuthenticationToken.unauthenticated(
                        target.email, "StrongPassword123!")))
        .isInstanceOf(org.springframework.security.authentication.LockedException.class);
  }

  @Test
  void configurationCollectionValidationReportAndDownloadsWorkTogether() throws Exception {
    readableApi("https://example.test/data");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "900002")
                .param("name", "Java Test Plant")
                .param("state", "Selangor")
                .param("capacityMw", "50")
                .param("isActive", "true")
                .param("ddqApiEndpoint", "https://example.test/data")
                .param("mmfApiEndpoint", ""))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var plant = db.list(LssPlant.class, "WHERE \"PlantId\"='900002'").getFirst();
    clearInvocations(client);
    var now = LocalDate.now(Policies.LOCAL).atStartOfDay();
    String payload =
        "[{\"timestamp\":\""
            + now
            + "\",\"power\":70},{\"timestamp\":\""
            + now.plusMinutes(15)
            + "\",\"power\":20}]";
    when(client.fetch("https://example.test/data"))
        .thenReturn(new PlantApiClient.Response(200, "application/json", payload));
    var job =
        collection.collect(
            plant.id, "Rolling 24 Hour", now, false, "test.otherAdmin", null, "INTEGRATION-1", 0);
    assertThat(job.status).isEqualTo("Completed");
    assertThat(db.list(RawApiResponse.class, "WHERE \"CollectionJobId\"=?", job.id)).hasSize(1);
    assertThat(db.list(DataAnomaly.class, "WHERE \"CollectionJobId\"=?", job.id)).hasSize(1);
    assertThat(
            collection.collect(
                    plant.id,
                    "Rolling 24 Hour",
                    now,
                    false,
                    "test.otherAdmin",
                    null,
                    "INTEGRATION-1",
                    0)
                .id)
        .isEqualTo(job.id);
    verify(client, times(1)).fetch(anyString());
    assertThatThrownBy(
            () ->
                collection.collect(
                    plant.id, "Rolling 24 Hour", now, false, "test.otherAdmin", null, null, 0))
        .isInstanceOf(IllegalArgumentException.class);
    mvc.perform(get("/collections/" + job.id).with(authentication(otherAdminAuth)))
        .andExpect(status().isOk());
    mvc.perform(
            get("/ddq-analytics")
                .param("from", now.toString())
                .param("to", now.plusDays(1).toString())
                .with(authentication(otherAdminAuth)))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("70.000000")));
    var report =
        reports.generate(
            "Rolling 24 Hour",
            Set.of(plant.id),
            now,
            15,
            null,
            null,
            "Integration report",
            "user:" + otherAdmin.id);
    assertThat(report.generationStatus).isEqualTo("Completed");
    assertThat(report.recordCount).isEqualTo(2);
    assertThat(report.anomalyCount).isEqualTo(2);
    var download = reports.download(report.id, "user:" + otherAdmin.id, "xlsx");
    try (var workbook = new XSSFWorkbook(new ByteArrayInputStream(download.data()))) {
      assertThat(workbook.getSheetAt(0).getRow(5).getCell(5).getNumericCellValue()).isEqualTo(50.0);
    }
    mvc.perform(get("/reports/" + report.id + "/download").with(authentication(otherAdminAuth)))
        .andExpect(status().isOk())
        .andExpect(
            header()
                .string(
                    "Content-Type",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    mvc.perform(get("/reports/" + report.id + "/download").with(authentication(adminAuth)))
        .andExpect(status().isForbidden());
    mvc.perform(
            get("/reports/" + report.id + "/download")
                .param("format", "anomalies.csv")
                .with(authentication(otherAdminAuth)))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("Registered plant capacity")));
    mvc.perform(get("/on-demand-analysis").with(authentication(otherAdminAuth)))
        .andExpect(status().isOk());
  }

  @Test
  void plantCanBeSavedWithoutAnyApiAndEditorOffersAllOptionalDatasets() throws Exception {
    mvc.perform(get("/admin/lss-plants/edit").with(authentication(adminAuth)))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("+ Add APIs")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("name=\"apiMonth4\"")))
        .andExpect(
            content()
                .string(
                    org.hamcrest.Matchers.not(
                        org.hamcrest.Matchers.containsString("Times use HH:mm"))));
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "91" + System.nanoTime() % 1000000)
                .param("name", "Plant without APIs")
                .param("capacityMw", "30")
                .param("isActive", "true"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var plant = db.list(LssPlant.class, "WHERE \"Name\"=?", "Plant without APIs").getFirst();
    assertThat(plant.ddqApiEndpoint).isEmpty();
    assertThat(db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", plant.id)).isEmpty();
  }

  @Test
  void plantApiLinksAreOptionalOwnedAndMonthlyDatasetIsUsedForCollection() throws Exception {
    readableApi("https://example.test/site/dayahead");
    readableApi("https://example.test/site/month1");
    readableApi("https://example.test/site/mmf");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "92" + System.nanoTime() % 1000000)
                .param("name", "Plant with API slots")
                .param("capacityMw", "30")
                .param("isActive", "true")
                .param("apiDayAhead", "https://example.test/site/dayahead")
                .param("apiMonth1", "https://example.test/site/month1")
                .param("apiMmf", "https://example.test/site/mmf"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var plant = db.list(LssPlant.class, "WHERE \"Name\"=?", "Plant with API slots").getFirst();
    var links = db.list(PlantApiConfiguration.class, "WHERE \"PlantId\"=?", plant.id);
    assertThat(links).hasSize(3);
    assertThat(links)
        .extracting(link -> link.dataType)
        .containsExactlyInAnyOrder("Day Ahead", "4 Month Ahead 1", "MMF");
    mvc.perform(
            get("/admin/lss-plants/edit")
                .param("id", plant.id.toString())
                .with(authentication(adminAuth)))
        .andExpect(
            content()
                .string(org.hamcrest.Matchers.containsString("https://example.test/site/month1")));
    var other = new LssPlant();
    other.plantId = "OTHER-" + System.nanoTime() % 1000000;
    other.name = "Separate plant";
    db.insert(other);
    var otherLink = new PlantApiConfiguration();
    otherLink.plantId = other.id;
    otherLink.dataType = "Day Ahead";
    otherLink.apiLink = "https://example.test/other/dayahead";
    db.insert(otherLink);
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("id", plant.id.toString())
                .param("plantId", plant.plantId)
                .param("name", plant.name)
                .param("capacityMw", "30")
                .param("apiDayAhead", "")
                .param("apiMonth1", "https://example.test/site/month1"))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    assertThat(
            db.list(
                PlantApiConfiguration.class,
                "WHERE \"PlantId\"=? AND \"DataType\"=?",
                plant.id,
                "Day Ahead"))
        .isEmpty();
    assertThat(db.find(PlantApiConfiguration.class, otherLink.id)).isPresent();
    assertThat(
            db.list(
                PlantApiConfiguration.class, "WHERE \"PlantId\"=? AND \"ApiType\"='MMF'", plant.id))
        .hasSize(1);
    clearInvocations(client);
    when(client.fetch("https://example.test/site/month1"))
        .thenReturn(
            new PlantApiClient.Response(
                200,
                "application/json",
                "[{\"timestamp\":\"2026-11-01T00:00:00\",\"power\":1.5}]"));
    collection.test(plant.id, "4 Month Ahead 1", "test.admin");
    verify(client).fetch("https://example.test/site/month1");
  }

  @Test
  void invalidPlantApiDoesNotPartiallySaveAndCsrfIsRequired() throws Exception {
    doThrow(new IllegalArgumentException("Use HTTPS"))
        .when(client)
        .validate("http://blocked.test/api");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "REJECT-API")
                .param("name", "Must not save")
                .param("capacityMw", "30")
                .param("apiMonth4", "http://blocked.test/api"))
        .andExpect(status().isBadRequest());
    assertThat(db.list(LssPlant.class, "WHERE \"PlantId\"=?", "REJECT-API")).isEmpty();
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .param("plantId", "NO-CSRF")
                .param("name", "Blocked")
                .param("capacityMw", "30"))
        .andExpect(status().isForbidden());
  }

  @Test
  void failedCollectionDoesNotPublishPartialReadings() {
    var plant = new LssPlant();
    plant.plantId = "FAIL-TEST";
    plant.name = "Failure Test";
    plant.ddqApiEndpoint = "https://example.test/invalid";
    db.insert(plant);
    when(client.fetch("https://example.test/invalid"))
        .thenReturn(new PlantApiClient.Response(200, "text/html", "<html>Sign in</html>"));
    var job =
        collection.collect(
            plant.id,
            "Day Ahead",
            LocalDateTime.now(Policies.LOCAL).withSecond(0).withNano(0),
            false,
            "test.otherAdmin",
            null,
            "FAILURE-CHECK",
            0);
    assertThat(job.status).isEqualTo("Failed");
    assertThat(db.list(SolarApiReading.class, "WHERE \"CollectionJobId\"=?", job.id)).isEmpty();
    assertThat(db.list(RawApiResponse.class, "WHERE \"CollectionJobId\"=?", job.id)).hasSize(1);
  }

  @Test
  void concurrentCommandsClaimTheSameJobOnlyOnce() throws Exception {
    var plant = new LssPlant();
    plant.plantId = "CONCURRENT-TEST";
    plant.name = "Concurrent test";
    plant.ddqApiEndpoint = "https://example.test/concurrent";
    db.insert(plant);
    var submission = LocalDate.now(Policies.LOCAL).atStartOfDay();
    when(client.fetch("https://example.test/concurrent"))
        .thenAnswer(
            call -> {
              Thread.sleep(150);
              return new PlantApiClient.Response(
                  200, "application/json", "{\"timestamp\":\"" + submission + "\",\"power\":10}");
            });
    try (var executor = java.util.concurrent.Executors.newFixedThreadPool(2)) {
      var gate = new java.util.concurrent.CountDownLatch(1);
      java.util.concurrent.Callable<CollectionJobEntity> task =
          () -> {
            gate.await();
            return collection.collect(
                plant.id,
                "Day Ahead",
                submission,
                false,
                "test.otherAdmin",
                null,
                "CONCURRENT-CHECK",
                0);
          };
      var a = executor.submit(task);
      var b = executor.submit(task);
      gate.countDown();
      assertThat(a.get().id).isEqualTo(b.get().id);
    }
    verify(client, times(1)).fetch("https://example.test/concurrent");
  }

  @Test
  void legacyReportsRegenerateUnderTheirOriginalOwner() throws Exception {
    var plant = new LssPlant();
    plant.plantId = "LEGACY-TEST";
    plant.name = "Legacy test";
    db.insert(plant);
    var job = new CollectionJobEntity();
    job.collectionId = "LEGACY-JOB";
    job.plantId = plant.id;
    job.status = "Completed";
    job.submissionDate = LocalDate.now(Policies.LOCAL);
    job.submissionDateTime = job.submissionDate.atStartOfDay();
    db.insert(job);
    var reading = new SolarApiReading();
    reading.plantId = plant.id;
    reading.collectionJobId = job.id;
    reading.dataType = "Day Ahead";
    reading.parameter = "Value";
    reading.value = BigDecimal.TEN;
    reading.submissionDateTime = job.submissionDateTime;
    reading.intervalStart = job.submissionDateTime;
    reading.intervalMinutes = 30;
    db.insert(reading);
    var report = new ReportExecution();
    report.reportId = "ODA-DDQ-" + UUID.randomUUID();
    report.reportType = "On-Demand";
    report.dataType = "DDQ";
    report.dataSelection = "Day Ahead";
    report.requestedBy = "user:" + otherAdmin.id;
    report.submissionDate = job.submissionDateTime;
    report.intervalMinutes = 30;
    report.description = "Historical report";
    report.generationStatus = "Completed";
    db.insert(report);
    var file = reports.download(report.id, "user:" + otherAdmin.id, "xlsx");
    try (var workbook = new XSSFWorkbook(new ByteArrayInputStream(file.data()))) {
      assertThat(workbook.getNumberOfSheets()).isEqualTo(1);
    }
    assertThatThrownBy(() -> reports.download(report.id, "user:" + admin.id, "xlsx"))
        .isInstanceOf(org.springframework.security.access.AccessDeniedException.class);
  }

  @Test
  void scheduledCollectionAndDeliveryAreIdempotentAndMailIsMocked() {
    var plant = new LssPlant();
    plant.plantId = "SCHEDULE-TEST";
    plant.name = "Scheduled test";
    plant.ddqApiEndpoint = "https://example.test/scheduled";
    db.insert(plant);
    var schedule = new CollectionSchedule();
    schedule.scheduleCode = "JAVA-SCHEDULE";
    schedule.plantId = plant.id;
    schedule.runTime = "00:00";
    schedule.name = "Daily collection";
    db.insert(schedule);
    var submission = LocalDate.now(Policies.LOCAL).atStartOfDay();
    when(client.fetch("https://example.test/scheduled"))
        .thenReturn(
            new PlantApiClient.Response(
                200, "application/json", "{\"timestamp\":\"" + submission + "\",\"power\":12}"));
    var mail = mock(MailDelivery.class);
    var workers = new ScheduledWorkers(db, collection, reports, mail);
    workers.collections();
    workers.collections();
    assertThat(db.list(CollectionJobEntity.class, "WHERE \"CollectionScheduleId\"=?", schedule.id))
        .hasSize(1);
    verify(client, times(1)).fetch("https://example.test/scheduled");
    var reportSchedule = new ReportScheduleEntity();
    reportSchedule.scheduleCode = "JAVA-REPORT-SCHEDULE";
    reportSchedule.name = "Scheduled report";
    reportSchedule.executionTime = "00:00";
    db.insert(reportSchedule);
    var recipient = new ReportScheduleRecipient();
    recipient.reportScheduleId = reportSchedule.id;
    recipient.email = "recipient@gmail.com";
    db.insert(recipient);
    workers.reports();
    workers.reports();
    var attempts =
        db.list(ReportDeliveryAttempt.class, "WHERE \"ReportScheduleId\"=?", reportSchedule.id);
    assertThat(attempts).hasSize(1);
    assertThat(attempts.getFirst().emailStatus).isEqualTo("Delivered");
    verify(mail, times(1))
        .send(eq(List.of("recipient@gmail.com")), eq("Scheduled report"), anyString(), any());
  }
}
