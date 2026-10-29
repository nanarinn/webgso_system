package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.*;
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
  @Autowired CollectionService collection;
  @Autowired ReportService reports;
  @MockitoBean PlantApiClient client;
  @MockitoBean ManualBatchService batches;
  ApplicationUser admin, otherAdmin;
  Authentication adminAuth, otherAdminAuth;

  @BeforeAll
  void users() {
    admin =
        accounts.register("test.admin", "admin@example.test", "Test Admin", "StrongPassword123!");
    admin.accessStatus = "Approved";
    db.update(admin);
    accounts.setRole(admin.id, "Admin");
    adminAuth = accounts.token(admin);
    otherAdmin =
        accounts.register(
            "test.otherAdmin", "otherAdmin@example.test", "Other Admin", "StrongPassword123!");
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
                .param("plantId", "NEW-CARD-API")
                .param("name", "New card plant")
                .param("capacityMw", "10")
                .param("apiMonth4", "https://example.test/new/month4")
                .param("apiMonth1", "https://example.test/not-yet-saved"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Plant and API saved.")));
    var plant = db.list(LssPlant.class, "WHERE \"PlantId\"=?", "NEW-CARD-API").getFirst();
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
            "/api/dashboard",
            "/admin/users"))
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
    mvc.perform(get("/").with(authentication(adminAuth)))
        .andExpect(status().isOk())
        .andExpect(view().name("admin-overview"))
        .andExpect(model().attribute("apiCount", (int) db.count(LssPlant.class)))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("22 Oct 2026, 07:15 MYT")))
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Admin accounts")));
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
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Admin accounts")));
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
                .param("identifier", admin.userName)
                .param("password", "StrongPassword123!")
                .param("_csrf", token.group(1)))
        .andExpect(redirectedUrl("/"));
    mvc.perform(
            post("/account/login")
                .param("identifier", admin.userName)
                .param("password", "StrongPassword123!"))
        .andExpect(status().isForbidden());
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
  void registrationCanSignInImmediatelyWithoutApproval() throws Exception {
    String name = "new." + System.nanoTime();
    String email = name + "@example.test";
    mvc.perform(
            post("/account/register")
                .with(csrf())
                .param("username", name)
                .param("email", email)
                .param("displayName", "New Admin")
                .param("password", "StrongPassword123!")
                .param("confirmPassword", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?registered=success"));
    var target = db.list(ApplicationUser.class, "WHERE \"UserName\"=?", name).getFirst();
    assertThat(target.accessStatus).isEqualTo("Approved");
    assertThat(accounts.roles(target.id)).containsExactly(Accounts.ADMIN);
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", email)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/"));
    String html =
        mvc.perform(get("/login").param("registered", "success"))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(html).contains("You can sign in now.").doesNotContain("must approve");
  }

  @Test
  void adminApprovalsUseOneRoleAndRevokedAccountsLoseTheirSession() throws Exception {
    var reviewer =
        accounts.register(
            "reviewer." + System.nanoTime(),
            "reviewer." + System.nanoTime() + "@example.test",
            "Reviewer",
            "StrongPassword123!");
    reviewer.accessStatus = "Approved";
    db.update(reviewer);
    accounts.setRole(reviewer.id, "Admin");
    assertThatThrownBy(
            () ->
                accounts.review(
                    accounts.token(reviewer), reviewer.id, "Inactive", "Admin", "Not allowed"))
        .isInstanceOf(IllegalArgumentException.class);
    var target =
        accounts.register(
            "revoke." + System.nanoTime(),
            "revoke." + System.nanoTime() + "@example.test",
            "Temporary Admin",
            "StrongPassword123!");
    mvc.perform(
            post("/admin/users/" + target.id + "/review")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("decision", "Approved")
                .param("role", "Staff"))
        .andExpect(status().isBadRequest());
    assertThat(db.find(ApplicationUser.class, target.id).orElseThrow().accessStatus)
        .isEqualTo("Approved");
    mvc.perform(
            post("/admin/users/" + target.id + "/review")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("decision", "Approved"))
        .andExpect(redirectedUrl("/admin/users"));
    assertThat(accounts.roles(target.id)).containsExactly(Accounts.ADMIN);
    target = db.find(ApplicationUser.class, target.id).orElseThrow();
    var oldAuth = accounts.token(target);
    accounts.review(adminAuth, target.id, "Inactive", "Admin", "Access ended");
    mvc.perform(get("/api/status").with(authentication(oldAuth)))
        .andExpect(status().isUnauthorized());
    String page =
        mvc.perform(get("/admin/users").with(authentication(adminAuth)))
            .andExpect(status().isOk())
            .andReturn()
            .getResponse()
            .getContentAsString();
    assertThat(page)
        .contains("Admin Accounts")
        .doesNotContain("SuperAdministrator", "Super Admin", "Staff", "name=\"role\"");
  }

  @Test
  @org.springframework.transaction.annotation.Transactional
  void existingRoleAssignmentsAreReadAsAdminWithoutRewritingAccounts() {
    for (String previous : List.of("Staff", "Administrator", "SuperAdministrator")) {
      var legacy =
          accounts.register(
              "legacy." + previous,
              previous + "@example.test",
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
            "unassigned.admin", "unassigned@example.test", "Unassigned", "StrongPassword123!");
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
            "lock." + System.nanoTime() + "@example.test",
            "Lock Test",
            "StrongPassword123!");
    target.accessStatus = "Approved";
    db.update(target);
    for (int i = 0; i < 5; i++)
      assertThatThrownBy(
              () ->
                  accounts.authenticate(
                      UsernamePasswordAuthenticationToken.unauthenticated(
                          target.userName, "wrong")))
          .isInstanceOf(org.springframework.security.authentication.BadCredentialsException.class);
    assertThatThrownBy(
            () ->
                accounts.authenticate(
                    UsernamePasswordAuthenticationToken.unauthenticated(
                        target.userName, "StrongPassword123!")))
        .isInstanceOf(org.springframework.security.authentication.LockedException.class);
  }

  @Test
  void configurationCollectionValidationReportAndDownloadsWorkTogether() throws Exception {
    readableApi("https://example.test/data");
    mvc.perform(
            post("/admin/lss-plants/save")
                .with(authentication(adminAuth))
                .with(csrf())
                .param("plantId", "JAVA-TEST")
                .param("name", "Java Test Plant")
                .param("state", "Selangor")
                .param("capacityMw", "50")
                .param("isActive", "true")
                .param("ddqApiEndpoint", "https://example.test/data")
                .param("mmfApiEndpoint", ""))
        .andExpect(redirectedUrl("/admin/lss-plants"));
    var plant = db.list(LssPlant.class, "WHERE \"PlantId\"='JAVA-TEST'").getFirst();
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
                .param("plantId", "NOAPI-" + System.nanoTime() % 1000000)
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
                .param("plantId", "API-" + System.nanoTime() % 1000000)
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
    recipient.email = "recipient@example.test";
    db.insert(recipient);
    workers.reports();
    workers.reports();
    var attempts =
        db.list(ReportDeliveryAttempt.class, "WHERE \"ReportScheduleId\"=?", reportSchedule.id);
    assertThat(attempts).hasSize(1);
    assertThat(attempts.getFirst().emailStatus).isEqualTo("Delivered");
    verify(mail, times(1))
        .send(eq(List.of("recipient@example.test")), eq("Scheduled report"), anyString(), any());
  }
}
