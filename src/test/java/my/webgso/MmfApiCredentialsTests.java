package my.webgso;

import static org.junit.jupiter.api.Assertions.*;
import java.net.URI;
import org.junit.jupiter.api.Test;
import org.springframework.test.util.ReflectionTestUtils;

class MmfApiCredentialsTests {
  @Test void credentialsAreRestrictedToTheConfiguredHttpsAuthorityAndPath() {
    var client = new PlantApiClient("", "", 1024);
    ReflectionTestUtils.setField(client, "mmfUrl", "https://example.test/api/mmf/BIDOR/latest");
    ReflectionTestUtils.setField(client, "mmfToken", "test-token");
    assertEquals("Bearer test-token", client.request(URI.create("https://example.test/api/mmf/BIDOR/latest?station=Weather_Station_SAE_3")).getFirstHeader("Authorization").getValue());
    for (String url : new String[] {"https://other.test/api/mmf/BIDOR/latest", "http://example.test/api/mmf/BIDOR/latest", "https://example.test:444/api/mmf/BIDOR/latest", "https://example.test/api/mmf/BIDOR/other", "https://example.test/api/mmf/BIDOR/latest/"})
      assertNull(client.request(URI.create(url)).getFirstHeader("Authorization"));
  }
}
