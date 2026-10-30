package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.Test;
import org.springframework.mock.env.MockEnvironment;

class ApiDailyDigestTests {
  @Test
  void disabledMailDoesNotQueueSummary() {
    var db = mock(Store.class);
    var history = mock(ApiTestHistoryService.class);
    new ApiDailyDigest(db, history, new MockEnvironment()).queueDailySummary();
    verifyNoInteractions(db, history);
  }

  @Test
  void summaryHasCurrentCountsAndLimitsAttentionList() {
    var rows = new ArrayList<Map<String, Object>>();
    rows.add(
        Map.of("name", "KMPR", "category", "Day Ahead", "tone", "success", "status", "Success"));
    rows.add(Map.of("name", "BDNS", "category", "MMF", "tone", "failed", "status", "Failed"));
    rows.add(
        Map.of(
            "name", "Other", "category", "Week Ahead", "tone", "untested", "status", "Not tested"));
    String body = ApiDailyDigest.body(rows, LocalDate.of(2026, 10, 30));
    assertThat(body)
        .contains(
            "Total configured APIs: 3",
            "Success: 1",
            "Failed: 1",
            "Not tested: 1",
            "BDNS / MMF",
            "Other / Week Ahead",
            "not a new API test");
    assertThat(ApiDailyDigest.body(List.of(), LocalDate.now())).contains("No APIs are configured");
  }

  @Test
  void dailyQueueSuppressesDuplicateEvenWhenPreviousDeliveryFailed() {
    var db = mock(Store.class);
    var history = mock(ApiTestHistoryService.class);
    var env =
        new MockEnvironment()
            .withProperty("webgso.mail.enabled", "true")
            .withProperty("webgso.api-alerts.enabled", "true")
            .withProperty("webgso.api-alerts.recipient", "admin@example.test");
    var user = new ApplicationUser();
    user.id = "owner";
    var previous = new UserAccessNotification();
    previous.deliveryStatus = "Failed";
    when(db.list(eq(ApplicationUser.class), anyString(), eq("admin@example.test")))
        .thenReturn(List.of(user));
    when(db.list(
            eq(UserAccessNotification.class),
            anyString(),
            eq("admin@example.test"),
            any(OffsetDateTime.class),
            any(OffsetDateTime.class)))
        .thenReturn(List.of(), List.of(previous));
    when(db.list(LssPlant.class, "")).thenReturn(List.of());
    when(db.list(PlantApiConfiguration.class, "")).thenReturn(List.of());
    when(history.latest()).thenReturn(Map.of());
    var digest = new ApiDailyDigest(db, history, env);
    digest.queueDailySummary();
    digest.queueDailySummary();
    var note = org.mockito.ArgumentCaptor.forClass(UserAccessNotification.class);
    verify(db).insert(note.capture());
    assertThat(note.getValue().notificationType).isEqualTo("ApiDailySummary");
    assertThat(note.getValue().deliveryStatus).isEqualTo("Pending");
    assertThat(note.getValue().email).isEqualTo("admin@example.test");
    verify(history, times(1)).latest();
  }
}
