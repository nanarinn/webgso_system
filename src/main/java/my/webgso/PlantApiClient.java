package my.webgso;

import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import org.apache.hc.client5.http.DnsResolver;
import org.apache.hc.client5.http.classic.methods.HttpGet;
import org.apache.hc.client5.http.config.RequestConfig;
import org.apache.hc.client5.http.impl.classic.HttpClients;
import org.apache.hc.client5.http.impl.io.PoolingHttpClientConnectionManagerBuilder;
import org.apache.hc.core5.util.Timeout;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

@Component
public class PlantApiClient {
  public record Response(int status, String contentType, String payload) {}

  private final Set<String> privateHosts, httpHosts;
  private final int maxBytes;

  public PlantApiClient(
      @Value("${webgso.api.allow-private-hosts:}") String privateHosts,
      @Value("${webgso.api.allow-http-hosts:}") String httpHosts,
      @Value("${webgso.api.maximum-bytes:10485760}") int maxBytes) {
    this.privateHosts = hosts(privateHosts);
    this.httpHosts = hosts(httpHosts);
    this.maxBytes = maxBytes;
  }

  private static Set<String> hosts(String value) {
    var hosts = new HashSet<String>();
    for (String h : value.split(","))
      if (!h.isBlank()) hosts.add(h.trim().toLowerCase(Locale.ROOT));
    return Set.copyOf(hosts);
  }

  public static boolean isPrivate(InetAddress ip) {
    byte[] b = ip.getAddress();
    return ip.isAnyLocalAddress()
        || ip.isLoopbackAddress()
        || ip.isLinkLocalAddress()
        || ip.isSiteLocalAddress()
        || ip.isMulticastAddress()
        || b.length == 16 && (b[0] & 0xfe) == 0xfc
        || b.length == 4
            && ((b[0] & 255) == 0
                || (b[0] & 255) == 100 && ((b[1] & 255) >= 64 && (b[1] & 255) <= 127)
                || (b[0] & 255) >= 224);
  }

  public URI validate(String url) {
    URI uri;
    try {
      uri = URI.create(url);
    } catch (Exception e) {
      throw new IllegalArgumentException("Enter a valid API URL.");
    }
    String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(Locale.ROOT);
    if (host.isBlank()
        || uri.getUserInfo() != null
        || uri.getFragment() != null
        || host.endsWith(".")
        || !(uri.getScheme().equals("https")
            || uri.getScheme().equals("http") && httpHosts.contains(host)))
      throw new IllegalArgumentException(
          "Use HTTPS. Internal or HTTP hosts must be explicitly allowed in server configuration.");
    return uri;
  }

  public Response fetch(String url) {
    URI uri = validate(url);
    DnsResolver resolver =
        new DnsResolver() {
          public InetAddress[] resolve(String host) throws UnknownHostException {
            var addresses = InetAddress.getAllByName(host);
            if (!privateHosts.contains(host.toLowerCase(Locale.ROOT)))
              for (var ip : addresses)
                if (isPrivate(ip))
                  throw new UnknownHostException("Private API destination is not allowed");
            return addresses;
          }

          public String resolveCanonicalHostname(String host) {
            return host;
          }
        };
    // Resolve and validate the same addresses used by the connection manager, including retries.
    try {
      resolver.resolve(uri.getHost());
    } catch (UnknownHostException e) {
      throw new IllegalArgumentException("API host could not be resolved or is not permitted.", e);
    }
    try (var manager =
            PoolingHttpClientConnectionManagerBuilder.create().setDnsResolver(resolver).build();
        var client =
            HttpClients.custom()
                .setConnectionManager(manager)
                .disableRedirectHandling()
                .disableAutomaticRetries()
                .setDefaultRequestConfig(
                    RequestConfig.custom()
                        .setConnectTimeout(Timeout.ofSeconds(10))
                        .setResponseTimeout(Timeout.ofSeconds(30))
                        .build())
                .build()) {
      return client.execute(
          new HttpGet(uri),
          response -> {
            int code = response.getCode();
            if (code < 200 || code >= 300)
              throw new IllegalArgumentException(
                  "The API returned HTTP " + code + ". Redirects are not followed.");
            var entity = response.getEntity();
            if (entity == null) throw new IllegalArgumentException("Empty API response");
            try (var stream = entity.getContent()) {
              byte[] bytes = stream.readNBytes(maxBytes + 1);
              if (bytes.length > maxBytes)
                throw new IllegalArgumentException("The API payload exceeds the size limit.");
              return new Response(
                  code,
                  Objects.toString(entity.getContentType(), "application/octet-stream"),
                  new String(bytes, StandardCharsets.UTF_8));
            }
          });
    } catch (IllegalArgumentException e) {
      throw e;
    } catch (Exception e) {
      throw new IllegalArgumentException(
          "API connection failed ("
              + e.getClass().getSimpleName()
              + "). Check connectivity and allowed hosts.",
          e);
    }
  }
}
