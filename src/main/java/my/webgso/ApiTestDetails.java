package my.webgso;

import com.fasterxml.jackson.databind.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import org.springframework.web.servlet.mvc.support.RedirectAttributes;

/** Bounded, escaped-by-the-view response details for the current test only. */
public final class ApiTestDetails {
  private ApiTestDetails() {}

  private static final ObjectMapper JSON = new ObjectMapper();
  private static final int PREVIEW_LIMIT = 12000;
  private static final Set<String> SECRET_FIELDS =
      Set.of(
          "password",
          "passwd",
          "secret",
          "token",
          "accesstoken",
          "refreshtoken",
          "idtoken",
          "apikey",
          "authorization",
          "clientsecret",
          "cookie",
          "setcookie");

  public static void add(RedirectAttributes flash, PlantApiClient.Response response) {
    details(response).forEach(flash::addFlashAttribute);
  }

  public static Map<String, Object> details(PlantApiClient.Response response) {
    var flash = new org.springframework.web.servlet.mvc.support.RedirectAttributesModelMap();
    flash.addFlashAttribute("apiTestHttpStatus", response.status());
    flash.addFlashAttribute("apiTestResponseMillis", response.elapsedMillis());
    flash.addFlashAttribute("apiTestContentType", response.contentType());
    flash.addFlashAttribute(
        "apiTestBytes", response.payload().getBytes(StandardCharsets.UTF_8).length);
    String preview = response.payload();
    try {
      JsonNode node = JSON.readTree(preview);
      if (node != null) {
        redact(node);
        preview = JSON.writerWithDefaultPrettyPrinter().writeValueAsString(node);
      }
    } catch (Exception ignored) {
      // Non-JSON responses remain plain text and are escaped by the Java component renderer.
    }
    boolean truncated = preview.length() > PREVIEW_LIMIT;
    flash.addFlashAttribute(
        "apiTestPreview", truncated ? preview.substring(0, PREVIEW_LIMIT) : preview);
    flash.addFlashAttribute("apiTestPreviewTruncated", truncated);
    return new LinkedHashMap<>(flash.getFlashAttributes());
  }

  private static void redact(JsonNode node) {
    if (node.isObject()) {
      var fields = node.fields();
      while (fields.hasNext()) {
        var field = fields.next();
        String key = field.getKey().replaceAll("[^a-zA-Z]", "").toLowerCase(Locale.ROOT);
        if (SECRET_FIELDS.contains(key))
          field.setValue(JSON.getNodeFactory().textNode("[redacted]"));
        else redact(field.getValue());
      }
    } else if (node.isArray()) node.forEach(ApiTestDetails::redact);
  }
}
