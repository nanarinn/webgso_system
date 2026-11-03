package my.webgso;

import static org.assertj.core.api.Assertions.*;
import static org.mockito.Mockito.*;

import java.time.Instant;
import java.util.List;
import java.util.Map;
import my.webgso.Models.ApplicationUser;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.client.authentication.OAuth2AuthenticationToken;
import org.springframework.security.oauth2.core.oidc.OidcIdToken;
import org.springframework.security.oauth2.core.oidc.user.DefaultOidcUser;
import org.springframework.security.web.context.HttpSessionSecurityContextRepository;

class GoogleLoginTests {
  @Test
  void linkingCallbackIsSingleUseAndNeverFallsBackToCreatingAnotherAccount() throws Exception {
    for (boolean matchingState : List.of(true, false)) {
      var google = mock(GoogleAccounts.class);
      var accounts = mock(Accounts.class);
      var links = mock(GoogleAccountLink.class);
      var intent =
          new GoogleAccountLink.Intent(
              "local-id", "stamp", Instant.now().plusSeconds(300), "expected");
      var user = new ApplicationUser();
      user.id = "local-id";
      when(links.current(intent)).thenReturn(user);
      var oidc = mock(org.springframework.security.oauth2.core.oidc.user.OidcUser.class);
      when(oidc.getSubject()).thenReturn("subject");
      when(oidc.getEmail()).thenReturn("other@gmail.com");
      when(oidc.getEmailVerified()).thenReturn(true);
      when(links.complete(intent, "subject", "other@gmail.com", true))
          .thenThrow(new IllegalArgumentException("Wrong Gmail"));
      var local =
          org.springframework.security.authentication.UsernamePasswordAuthenticationToken
              .authenticated(
                  new Accounts.Principal("local-id", "local", "Local", "stamp"),
                  null,
                  List.of(new SimpleGrantedAuthority("ROLE_Admin")));
      when(accounts.token(user)).thenReturn(local);
      var request = new MockHttpServletRequest();
      request.getSession().setAttribute(GoogleAccountLink.SESSION_KEY, intent);
      request.setParameter("state", matchingState ? "expected" : "incorrect");
      var response = new MockHttpServletResponse();
      var auth =
          new OAuth2AuthenticationToken(
              oidc, List.of(new SimpleGrantedAuthority("OIDC_USER")), "google");
      new GoogleLogin(google, accounts, links).success(request, response, auth);
      verifyNoInteractions(google);
      if (matchingState) {
        assertThat(response.getRedirectedUrl()).isEqualTo("/account/profile?googleLink=error");
        assertThat(request.getSession().getAttribute(GoogleAccountLink.SESSION_KEY)).isNull();
        assertThat(SecurityContextHolder.getContext().getAuthentication()).isSameAs(local);
      } else {
        assertThat(response.getRedirectedUrl()).isEqualTo("/login?google=error");
        assertThat(request.getSession(false)).isNull();
        verifyNoInteractions(links);
      }
      SecurityContextHolder.clearContext();
    }
  }

  @AfterEach
  void clear() {
    SecurityContextHolder.clearContext();
  }

  @Test
  void onlyApprovedGoogleAccountsBecomeLocalAuthenticatedSessions() throws Exception {
    for (String status : List.of("Pending", "Approved")) {
      var google = mock(GoogleAccounts.class);
      var accounts = mock(Accounts.class);
      var user = new ApplicationUser();
      user.id = "user1";
      user.accessStatus = status;
      when(google.resolve("subject1", "new@gmail.com", true, "New User")).thenReturn(user);
      var local =
          org.springframework.security.authentication.UsernamePasswordAuthenticationToken
              .authenticated(
                  new Accounts.Principal("user1", "google.user", "New User", "stamp"),
                  null,
                  List.of(new SimpleGrantedAuthority("ROLE_Admin")));
      when(accounts.token(user)).thenReturn(local);
      var oidc =
          new DefaultOidcUser(
              List.of(new SimpleGrantedAuthority("OIDC_USER")),
              new OidcIdToken(
                  "test-token",
                  Instant.now(),
                  Instant.now().plusSeconds(60),
                  Map.of(
                      "sub",
                      "subject1",
                      "email",
                      "new@gmail.com",
                      "email_verified",
                      true,
                      "name",
                      "New User")));
      var auth = new OAuth2AuthenticationToken(oidc, oidc.getAuthorities(), "google");
      var request = new MockHttpServletRequest();
      request.getSession();
      var response = new MockHttpServletResponse();
      SecurityContextHolder.getContext().setAuthentication(auth);
      new GoogleLogin(google, accounts, mock(GoogleAccountLink.class))
          .success(request, response, auth);
      if (status.equals("Approved")) {
        assertThat(response.getRedirectedUrl()).isEqualTo("/");
        var context =
            (org.springframework.security.core.context.SecurityContext)
                request
                    .getSession()
                    .getAttribute(HttpSessionSecurityContextRepository.SPRING_SECURITY_CONTEXT_KEY);
        assertThat(context.getAuthentication()).isSameAs(local);
      } else {
        assertThat(response.getRedirectedUrl()).isEqualTo("/login?google=pending");
        assertThat(request.getSession(false)).isNull();
        assertThat(SecurityContextHolder.getContext().getAuthentication()).isNull();
      }
      SecurityContextHolder.clearContext();
    }
  }
}
