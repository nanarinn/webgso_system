package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import java.util.*;
import my.webgso.Models.*;
import org.junit.jupiter.api.Test;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.mock.env.MockEnvironment;

class ApiEmailAlertsTests {
  @Test
  void alertsOnlyOnFirstFailureAndChanges() {
    assertThat(ApiEmailAlerts.changed(null, false)).isTrue();
    assertThat(ApiEmailAlerts.changed("Success", false)).isTrue();
    assertThat(ApiEmailAlerts.changed("Failed", false)).isFalse();
    assertThat(ApiEmailAlerts.changed("Failed", true)).isTrue();
    assertThat(ApiEmailAlerts.changed("Success", true)).isFalse();
    assertThat(ApiEmailAlerts.changed(null, true)).isFalse();
  }

  @Test
  void guidanceDistinguishesNoResponseInvalidDataAndHttpErrors() {
    assertThat(ApiEmailAlerts.guidance(null)).contains("network/VPN");
    assertThat(ApiEmailAlerts.guidance(200)).contains("data validation failed");
    assertThat(ApiEmailAlerts.guidance(401)).contains("authentication");
    assertThat(ApiEmailAlerts.guidance(404)).contains("endpoint path");
    assertThat(ApiEmailAlerts.guidance(429)).contains("rate limit");
    assertThat(ApiEmailAlerts.guidance(503)).contains("provider");
  }

  @Test
  void sendsQueuedDailySummaryAndRecordsAcceptance() {
    var db = mock(Store.class);
    var mail = mock(MailDelivery.class);
    var jdbc = mock(JdbcTemplate.class);
    var env =
        new MockEnvironment()
            .withProperty("webgso.mail.enabled", "true")
            .withProperty("webgso.api-alerts.enabled", "true");
    var note = new UserAccessNotification();
    note.id = 2L;
    note.email = "admin@example.test";
    note.subject = "Daily API summary";
    note.body = "Summary";
    note.notificationType = "ApiDailySummary";
    when(db.list(eq(UserAccessNotification.class), anyString())).thenReturn(List.of(note));
    when(db.jdbc()).thenReturn(jdbc);
    when(jdbc.update(anyString(), eq(2L))).thenReturn(1);
    new ApiEmailAlerts(db, mail, env).deliver();
    verify(mail).send(List.of("admin@example.test"), "Daily API summary", "Summary", null);
    assertThat(note.deliveryStatus).isEqualTo("Delivered");
    assertThat(note.sentAt).isNotNull();
    verify(db).update(note);
  }

  @Test
  void queuesOnlyForConfiguredApprovedRecipientAndNoRawResponse() {
    var db = mock(Store.class);
    var mail = mock(MailDelivery.class);
    var env =
        new MockEnvironment()
            .withProperty("webgso.api-alerts.enabled", "true")
            .withProperty("webgso.api-alerts.recipient", "admin@example.test");
    var p = new LssPlant();
    p.id = 1;
    p.name = "KMPR";
    p.plantId = "01";
    var user = new ApplicationUser();
    user.id = "user";
    when(db.find(LssPlant.class, 1)).thenReturn(Optional.of(p));
    when(db.list(eq(ApplicationUser.class), anyString(), eq("admin@example.test")))
        .thenReturn(List.of(user));
    var alerts = new ApiEmailAlerts(db, mail, env);
    alerts.record(1, "Day Ahead", null, false, 503);
    var note = org.mockito.ArgumentCaptor.forClass(UserAccessNotification.class);
    verify(db).insert(note.capture());
    assertThat(note.getValue().email).isEqualTo("admin@example.test");
    assertThat(note.getValue().body).contains("KMPR", "Day Ahead", "503");
    assertThat(note.getValue().deliveryStatus).isEqualTo("Pending");
    alerts.record(1, "Day Ahead", "Failed", false, 503);
    verify(db, times(1)).insert(any(UserAccessNotification.class));
    verifyNoInteractions(mail);
  }

  @Test
  void disabledAlertsDoNothingAndDeliveryFailureDoesNotThrow() {
    var db = mock(Store.class);
    var mail = mock(MailDelivery.class);
    var env = new MockEnvironment();
    var alerts = new ApiEmailAlerts(db, mail, env);
    alerts.record(1, "MMF", null, false, null);
    alerts.deliver();
    verifyNoInteractions(db, mail);
    env.withProperty("webgso.api-alerts.enabled", "true")
        .withProperty("webgso.mail.enabled", "true");
    var jdbc = mock(JdbcTemplate.class);
    when(db.jdbc()).thenReturn(jdbc);
    var note = new UserAccessNotification();
    note.id = 1L;
    note.email = "admin@example.test";
    note.subject = "Failed";
    note.body = "API failed";
    when(db.list(eq(UserAccessNotification.class), anyString())).thenReturn(List.of(note));
    when(jdbc.update(anyString(), eq(1L))).thenReturn(1);
    doThrow(new IllegalArgumentException("SMTP failure"))
        .when(mail)
        .send(anyList(), anyString(), anyString(), isNull());
    assertThatCode(alerts::deliver).doesNotThrowAnyException();
    assertThat(note.deliveryStatus).isEqualTo("Failed");
    verify(db).update(note);
  }
}
