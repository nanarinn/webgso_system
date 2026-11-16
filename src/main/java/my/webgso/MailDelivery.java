package my.webgso;

import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.mail.internet.InternetAddress;
import java.io.ByteArrayOutputStream;
import java.net.*;
import java.net.http.*;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.*;
import org.springframework.core.env.Environment;
import org.springframework.core.io.ByteArrayResource;
import org.springframework.mail.javamail.*;
import org.springframework.stereotype.Service;
import software.amazon.awssdk.core.SdkBytes;
import software.amazon.awssdk.regions.Region;
import software.amazon.awssdk.services.sesv2.SesV2Client;
import software.amazon.awssdk.services.sesv2.model.*;

@Service
public class MailDelivery {
  private final Environment env;
  private final JavaMailSender sender;
  private final ObjectMapper json;

  public MailDelivery(Environment env, JavaMailSender sender, ObjectMapper json) {
    this.env = env;
    this.sender = sender;
    this.json = json;
  }

  public void send(
      List<String> recipients, String subject, String body, ReportService.Download attachment) {
    if (!env.getProperty("webgso.mail.enabled", Boolean.class, false))
      throw new IllegalArgumentException(
          "Email delivery is disabled. Configure the email service before enabling it.");
    if (recipients.isEmpty()) throw new IllegalArgumentException("No email recipients configured");
    try {
      for (String recipient : recipients) new InternetAddress(recipient, true).validate();
      boolean ses = "ses".equalsIgnoreCase(env.getProperty("webgso.mail.provider", "smtp"));
      String tenant = env.getProperty("WEBGSO_GRAPH_TENANT_ID", "");
      if (!ses && !tenant.isBlank()) {
        graph(tenant, recipients, subject, body, attachment);
        return;
      }
      var message = sender.createMimeMessage();
      var helper = new MimeMessageHelper(message, true, "UTF-8");
      helper.setFrom(env.getRequiredProperty("webgso.mail.from"));
      helper.setTo(recipients.toArray(String[]::new));
      helper.setSubject(subject);
      helper.setText(body, false);
      if (attachment != null)
        helper.addAttachment(
            attachment.filename(),
            new ByteArrayResource(attachment.data()),
            attachment.contentType());
      if (ses) {
        message.saveChanges();
        var bytes = new ByteArrayOutputStream();
        message.writeTo(bytes);
        try (var client = sesClient()) {
          client.sendEmail(SendEmailRequest.builder()
              .fromEmailAddress(env.getRequiredProperty("webgso.mail.from"))
              .destination(Destination.builder().toAddresses(recipients).build())
              .content(EmailContent.builder().raw(RawMessage.builder()
                  .data(SdkBytes.fromByteArray(bytes.toByteArray())).build()).build()).build());
        }
      } else sender.send(message);
    } catch (Exception e) {
      throw new IllegalArgumentException(
          "Email delivery failed ("
              + e.getClass().getSimpleName()
              + "). Check the configured email service.",
          e);
    }
  }

  SesV2Client sesClient() {
    return SesV2Client.builder()
        .region(Region.of(env.getProperty("webgso.mail.ses-region", "ap-southeast-5")))
        .overrideConfiguration(c -> c.apiCallTimeout(Duration.ofSeconds(45))
            .apiCallAttemptTimeout(Duration.ofSeconds(15)))
        .build();
  }

  private static String form(String value) {
    return URLEncoder.encode(value, StandardCharsets.UTF_8);
  }

  private void graph(
      String tenant,
      List<String> recipients,
      String subject,
      String body,
      ReportService.Download attachment)
      throws Exception {
    if (!tenant.matches("[a-zA-Z0-9.-]+"))
      throw new IllegalArgumentException("Invalid tenant identifier");
    try (var client =
        HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .followRedirects(HttpClient.Redirect.NEVER)
            .build()) {
      String data =
          "grant_type=client_credentials&scope="
              + form("https://graph.microsoft.com/.default")
              + "&client_id="
              + form(env.getRequiredProperty("WEBGSO_GRAPH_CLIENT_ID"))
              + "&client_secret="
              + form(env.getRequiredProperty("WEBGSO_GRAPH_CLIENT_SECRET"));
      var token =
          client.send(
              HttpRequest.newBuilder(
                      URI.create(
                          "https://login.microsoftonline.com/" + tenant + "/oauth2/v2.0/token"))
                  .timeout(Duration.ofSeconds(30))
                  .header("Content-Type", "application/x-www-form-urlencoded")
                  .POST(HttpRequest.BodyPublishers.ofString(data))
                  .build(),
              HttpResponse.BodyHandlers.ofString());
      if (token.statusCode() != 200)
        throw new IllegalArgumentException("Graph token request failed");
      String access = json.readTree(token.body()).path("access_token").asText();
      Map<String, Object> message = new LinkedHashMap<>();
      message.put("subject", subject);
      message.put("body", Map.of("contentType", "Text", "content", body));
      message.put(
          "toRecipients",
          recipients.stream().map(r -> Map.of("emailAddress", Map.of("address", r))).toList());
      if (attachment != null) {
        if (attachment.data().length > 2_500_000)
          throw new IllegalArgumentException("Attachment exceeds the Graph simple-send limit");
        message.put(
            "attachments",
            List.of(
                Map.of(
                    "@odata.type",
                    "#microsoft.graph.fileAttachment",
                    "name",
                    attachment.filename(),
                    "contentType",
                    attachment.contentType(),
                    "contentBytes",
                    Base64.getEncoder().encodeToString(attachment.data()))));
      }
      String mailbox = env.getRequiredProperty("webgso.mail.from");
      var result =
          client.send(
              HttpRequest.newBuilder(
                      URI.create(
                          "https://graph.microsoft.com/v1.0/users/" + form(mailbox) + "/sendMail"))
                  .timeout(Duration.ofSeconds(45))
                  .header("Authorization", "Bearer " + access)
                  .header("Content-Type", "application/json")
                  .POST(
                      HttpRequest.BodyPublishers.ofString(
                          json.writeValueAsString(
                              Map.of("message", message, "saveToSentItems", true))))
                  .build(),
              HttpResponse.BodyHandlers.discarding());
      if (result.statusCode() != 202)
        throw new IllegalArgumentException("Graph did not accept the email");
    }
  }
}
