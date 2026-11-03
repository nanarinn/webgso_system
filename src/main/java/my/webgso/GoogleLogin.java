package my.webgso;

import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.oauth2.client.CommonOAuth2Provider;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.client.registration.ClientRegistrationRepository;
import org.springframework.security.oauth2.client.registration.InMemoryClientRegistrationRepository;
import org.springframework.security.oauth2.client.web.DefaultOAuth2AuthorizationRequestResolver;
import org.springframework.security.oauth2.client.web.OAuth2AuthorizationRequestResolver;
import org.springframework.security.oauth2.core.endpoint.OAuth2AuthorizationRequest;
import org.springframework.security.oauth2.core.oidc.user.OidcUser;
import org.springframework.security.web.context.HttpSessionSecurityContextRepository;
import org.springframework.stereotype.Component;

@Component
public class GoogleLogin {
  private final GoogleAccounts googleAccounts;
  private final Accounts accounts;
  private final GoogleAccountLink links;

  public GoogleLogin(GoogleAccounts googleAccounts, Accounts accounts, GoogleAccountLink links) {
    this.googleAccounts = googleAccounts;
    this.accounts = accounts;
    this.links = links;
  }

  public OAuth2AuthorizationRequestResolver resolver(ClientRegistrationRepository clients) {
    var delegate = new DefaultOAuth2AuthorizationRequestResolver(clients, "/oauth2/authorization");
    return new OAuth2AuthorizationRequestResolver() {
      public OAuth2AuthorizationRequest resolve(HttpServletRequest request) {
        return bind(request, delegate.resolve(request));
      }

      public OAuth2AuthorizationRequest resolve(HttpServletRequest request, String id) {
        return bind(request, delegate.resolve(request, id));
      }

      private OAuth2AuthorizationRequest bind(
          HttpServletRequest request, OAuth2AuthorizationRequest authorization) {
        var session = request.getSession(false);
        if (authorization != null
            && session != null
            && session.getAttribute(GoogleAccountLink.SESSION_KEY)
                instanceof GoogleAccountLink.Intent intent) {
          session.setAttribute(
              GoogleAccountLink.SESSION_KEY, intent.withState(authorization.getState()));
          return OAuth2AuthorizationRequest.from(authorization)
              .additionalParameters(parameters -> parameters.put("prompt", "select_account"))
              .build();
        }
        return authorization;
      }
    };
  }

  public void success(HttpServletRequest request, HttpServletResponse response, Authentication auth)
      throws IOException {
    var session = request.getSession(false);
    if (session != null
        && session.getAttribute(GoogleAccountLink.SESSION_KEY)
            instanceof GoogleAccountLink.Intent intent) {
      session.removeAttribute(GoogleAccountLink.SESSION_KEY);
      try {
        if (intent.state() == null || !intent.state().equals(request.getParameter("state")))
          throw new IllegalArgumentException("Invalid Google linking state");
        var user = links.current(intent);
        String outcome = "linked";
        try {
          var identity = (OidcUser) auth.getPrincipal();
          user =
              links.complete(
                  intent, identity.getSubject(), identity.getEmail(), identity.getEmailVerified());
        } catch (RuntimeException e) {
          outcome = "error";
        }
        var context = SecurityContextHolder.createEmptyContext();
        context.setAuthentication(accounts.token(user));
        SecurityContextHolder.setContext(context);
        new HttpSessionSecurityContextRepository().saveContext(context, request, response);
        response.sendRedirect("/account/profile?googleLink=" + outcome);
      } catch (RuntimeException e) {
        clear(request);
        response.sendRedirect("/login?google=error");
      }
      return;
    }
    String result;
    try {
      var identity = (OidcUser) auth.getPrincipal();
      var user =
          googleAccounts.resolve(
              identity.getSubject(),
              identity.getEmail(),
              identity.getEmailVerified(),
              identity.getFullName());
      if ("Approved".equals(user.accessStatus)) {
        var context = SecurityContextHolder.createEmptyContext();
        context.setAuthentication(accounts.token(user));
        SecurityContextHolder.setContext(context);
        new HttpSessionSecurityContextRepository().saveContext(context, request, response);
        response.sendRedirect("/");
        return;
      }
      result = "pending";
    } catch (GoogleAccounts.ExistingEmailException e) {
      result = "existing";
    } catch (RuntimeException e) {
      result = "error";
    }
    clear(request);
    response.sendRedirect("/login?google=" + result);
  }

  public static void clear(HttpServletRequest request) {
    SecurityContextHolder.clearContext();
    if (request.getSession(false) != null) request.getSession(false).invalidate();
  }

  @Configuration
  @ConditionalOnProperty(name = "webgso.google.enabled", havingValue = "true")
  static class Registration {
    @Bean
    ClientRegistrationRepository googleClients(
        @Value("${webgso.google.client-id}") String id,
        @Value("${webgso.google.client-secret}") String secret,
        @Value("${webgso.google.redirect-uri}") String redirect) {
      if (id.isBlank() || secret.isBlank())
        throw new IllegalStateException(
            "Configure Google OAuth credentials before enabling Google login.");
      return new InMemoryClientRegistrationRepository(
          CommonOAuth2Provider.GOOGLE
              .getBuilder("google")
              .clientId(id)
              .clientSecret(secret)
              .scope("openid", "profile", "email")
              .redirectUri(redirect)
              .build());
    }
  }
}
