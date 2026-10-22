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
  ApplicationUser admin, staff;
  Authentication adminAuth, staffAuth;

  @BeforeAll
  void users() {
    admin =
        accounts.register("test.admin", "admin@example.test", "Test Admin", "StrongPassword123!");
    admin.accessStatus = "Approved";
    db.update(admin);
    accounts.setRole(admin.id, "SuperAdministrator");
    adminAuth = accounts.token(admin);
    staff =
        accounts.register("test.staff", "staff@example.test", "Test Staff", "StrongPassword123!");
    staff.accessStatus = "Approved";
    db.update(staff);
    accounts.setRole(staff.id, "Staff");
    staffAuth = accounts.token(staff);
  }

  @BeforeEach
  void resetApi() {
    reset(client);
  }

  @Test
  void authenticationAndEveryPageRenderWithCorrectAccess() throws Exception {
    mvc.perform(get("/")).andExpect(status().is3xxRedirection());
    mvc.perform(get("/api/status")).andExpect(status().isUnauthorized());
    mvc.perform(get("/health")).andExpect(status().isOk());
    mvc.perform(get("/login"))
        .andExpect(status().isOk())
        .andExpect(content().string(org.hamcrest.Matchers.containsString("Sign in to your workspace")));
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
      mvc.perform(get(resource.path()).with(authentication(staffAuth)))
          .andExpect(status().isForbidden());
    }
    mvc.perform(post("/collections/run").with(authentication(staffAuth)))
        .andExpect(status().isForbidden());
  }

  @Test
  void registrationRemainsPendingAndCannotSignInUntilReviewed() throws Exception {
    String name = "pending." + System.nanoTime();
    mvc.perform(
            post("/account/register")
                .with(csrf())
                .param("username", name)
                .param("email", name + "@example.test")
                .param("displayName", "New Staff")
                .param("password", "StrongPassword123!")
                .param("confirmPassword", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?registered=pending"));
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", name)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/login?error=true"));
    var target = db.list(ApplicationUser.class, "WHERE \"UserName\"=?", name).getFirst();
    accounts.review(adminAuth, target.id, "Approved", "Staff", "Approved for integration check");
    mvc.perform(
            post("/account/login")
                .with(csrf())
                .param("identifier", name)
                .param("password", "StrongPassword123!"))
        .andExpect(redirectedUrl("/"));
  }

  @Test
  void administratorsCannotEscalatePrivilegesAndRevokedAccountsLoseTheirSession() throws Exception {
    var reviewer =
        accounts.register(
            "reviewer." + System.nanoTime(),
            "reviewer." + System.nanoTime() + "@example.test",
            "Reviewer",
            "StrongPassword123!");
    reviewer.accessStatus = "Approved";
    db.update(reviewer);
    accounts.setRole(reviewer.id, "Administrator");
    assertThatThrownBy(
            () ->
                accounts.review(
                    accounts.token(reviewer), admin.id, "Inactive", "Staff", "Not allowed"))
        .isInstanceOf(org.springframework.security.access.AccessDeniedException.class);
    assertThatThrownBy(
            () ->
                accounts.review(
                    accounts.token(reviewer), staff.id, "Approved", "Administrator", "Not allowed"))
        .isInstanceOf(org.springframework.security.access.AccessDeniedException.class);
    var target =
        accounts.register(
            "revoke." + System.nanoTime(),
            "revoke." + System.nanoTime() + "@example.test",
            "Temporary Staff",
            "StrongPassword123!");
    target.accessStatus = "Approved";
    db.update(target);
    accounts.setRole(target.id, "Staff");
    var oldAuth = accounts.token(target);
    accounts.review(adminAuth, target.id, "Inactive", "Staff", "Access ended");
    mvc.perform(get("/api/status").with(authentication(oldAuth)))
        .andExpect(status().isUnauthorized());
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
            plant.id, "Rolling 24 Hour", now, false, "test.staff", null, "INTEGRATION-1", 0);
    assertThat(job.status).isEqualTo("Completed");
    assertThat(db.list(RawApiResponse.class, "WHERE \"CollectionJobId\"=?", job.id)).hasSize(1);
    assertThat(db.list(DataAnomaly.class, "WHERE \"CollectionJobId\"=?", job.id)).hasSize(1);
    assertThat(
            collection.collect(
                    plant.id, "Rolling 24 Hour", now, false, "test.staff", null, "INTEGRATION-1", 0)
                .id)
        .isEqualTo(job.id);
    verify(client, times(1)).fetch(anyString());
    assertThatThrownBy(
            () ->
                collection.collect(
                    plant.id, "Rolling 24 Hour", now, false, "test.staff", null, null, 0))
        .isInstanceOf(IllegalArgumentException.class);
    mvc.perform(get("/collections/" + job.id).with(authentication(staffAuth)))
        .andExpect(status().isOk());
    mvc.perform(
            get("/ddq-analytics")
                .param("from", now.toString())
                .param("to", now.plusDays(1).toString())
                .with(authentication(staffAuth)))
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
            "user:" + staff.id);
    assertThat(report.generationStatus).isEqualTo("Completed");
    assertThat(report.recordCount).isEqualTo(2);
    assertThat(report.anomalyCount).isEqualTo(2);
    var download = reports.download(report.id, "user:" + staff.id, "xlsx");
    try (var workbook = new XSSFWorkbook(new ByteArrayInputStream(download.data()))) {
      assertThat(workbook.getSheetAt(0).getRow(5).getCell(5).getNumericCellValue()).isEqualTo(50.0);
    }
    mvc.perform(get("/reports/" + report.id + "/download").with(authentication(staffAuth)))
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
                .with(authentication(staffAuth)))
        .andExpect(status().isOk())
        .andExpect(
            content().string(org.hamcrest.Matchers.containsString("Registered plant capacity")));
    mvc.perform(get("/on-demand-analysis").with(authentication(staffAuth)))
        .andExpect(status().isOk());
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
            "test.staff",
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
                "test.staff",
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
    report.requestedBy = "user:" + staff.id;
    report.submissionDate = job.submissionDateTime;
    report.intervalMinutes = 30;
    report.description = "Historical report";
    report.generationStatus = "Completed";
    db.insert(report);
    var file = reports.download(report.id, "user:" + staff.id, "xlsx");
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
