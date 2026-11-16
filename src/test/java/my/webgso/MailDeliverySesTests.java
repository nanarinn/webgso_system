package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.mail.javamail.JavaMailSenderImpl;
import org.springframework.mock.env.MockEnvironment;
import software.amazon.awssdk.services.sesv2.SesV2Client;
import software.amazon.awssdk.services.sesv2.model.SendEmailRequest;

class MailDeliverySesTests {
  @Test
  void verificationUsesSesWithConfiguredSenderAndRecipient() {
    var env = new MockEnvironment().withProperty("webgso.mail.enabled", "true")
        .withProperty("webgso.mail.provider", "ses")
        .withProperty("webgso.mail.from", "developer@sae-malaysia.com");
    var client = mock(SesV2Client.class);
    var delivery = spy(new MailDelivery(env, new JavaMailSenderImpl(), new ObjectMapper()));
    doReturn(client).when(delivery).sesClient();
    delivery.send(List.of("suaid.ishak@sae-malaysia.com"), "Verify your email",
        "Open https://example.test/account/verify-email?token=test", null);
    var request = ArgumentCaptor.forClass(SendEmailRequest.class);
    verify(client).sendEmail(request.capture());
    assertThat(request.getValue().fromEmailAddress()).isEqualTo("developer@sae-malaysia.com");
    assertThat(request.getValue().destination().toAddresses()).containsExactly("suaid.ishak@sae-malaysia.com");
    assertThat(request.getValue().content().raw().data().asUtf8String())
        .contains("Verify your email", "https://example.test/account/verify-email?token=test");
    verify(client).close();
  }

  @Test
  void disabledDeliveryNeverCreatesSesClient() {
    var delivery = spy(new MailDelivery(new MockEnvironment(), new JavaMailSenderImpl(), new ObjectMapper()));
    assertThatThrownBy(() -> delivery.send(List.of("user@example.test"), "Verify", "Body", null))
        .isInstanceOf(IllegalArgumentException.class);
    verify(delivery, never()).sesClient();
  }
}
