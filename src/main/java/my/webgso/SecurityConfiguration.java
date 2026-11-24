package my.webgso;

import jakarta.servlet.*;
import jakarta.servlet.http.*;
import java.io.IOException;
import java.util.Objects;
import org.springframework.context.annotation.*;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.AnonymousAuthenticationFilter;
import org.springframework.security.web.csrf.CsrfException;
import org.springframework.web.filter.OncePerRequestFilter;

@Configuration
public class SecurityConfiguration {
  @Bean
  SecurityFilterChain security(HttpSecurity http, Accounts accounts, Store db) throws Exception {
    http.authenticationProvider(accounts)
        .authorizeHttpRequests(
            auth -> {
              auth.requestMatchers(
                        "/oauth2/**", "/login/oauth2/**", "/account/profile/google/link")
                    .denyAll()
                    .requestMatchers(
                        "/login",
                        "/account/login",
                        "/account/google",
                        "/styles/**",
                        "/bootstrap/**",
                        "/scripts/**",
                        "/*.png",
                        "/*.svg",
                        "/health",
                        "/api/test-data/mmf/**",
                        "/error")
                    .permitAll();
              if (accounts.environmentMode()) {
                auth.requestMatchers(
                        "/",
                        "/lss-api",
                        "/lss-api/**",
                        "/admin/collection-schedules",
                        "/admin/collection-schedules/**",
                        "/account/logout")
                    .hasRole("Admin")
                    .anyRequest()
                    .denyAll();
              } else {
                auth.requestMatchers(
                        "/register",
                        "/account/register",
                        "/account/verify-email",
                        "/account/resend-verification",
                        "/account/forgot-password",
                        "/account/reset-password")
                    .permitAll()
                    .requestMatchers("/admin/users", "/admin/users/**")
                    .denyAll()
                    .requestMatchers("/admin/**", "/api/admin/**")
                    .hasRole("Admin")
                    .anyRequest()
                    .hasRole("Admin");
              }
            })
        .formLogin(
            form ->
                form.loginPage("/login")
                    .loginProcessingUrl("/account/login")
                    .usernameParameter("identifier")
                    .defaultSuccessUrl("/", true)
                    .failureUrl("/login?error=true")
                    .permitAll())
        .logout(
            logout ->
                logout
                    .logoutUrl("/account/logout")
                    .logoutSuccessUrl("/login?logout=true")
                    .invalidateHttpSession(true)
                    .deleteCookies("JSESSIONID"))
        .exceptionHandling(
            ex ->
                ex.accessDeniedHandler(
                        (request, response, error) -> {
                          // Reject stale login submissions without replaying credentials or
                          // bypassing
                          // CSRF. A fresh GET renders a new session-bound token.
                          if (error instanceof CsrfException
                              && "POST".equals(request.getMethod())
                              && (request.getContextPath() + "/account/login")
                                  .equals(request.getRequestURI())) {
                            response.sendRedirect(
                                request.getContextPath() + "/login?reason=form-expired");
                          } else {
                            response.sendError(HttpServletResponse.SC_FORBIDDEN);
                          }
                        })
                    .authenticationEntryPoint(
                        (request, response, error) -> {
                          if (request.getRequestURI().startsWith("/api/")) response.sendError(401);
                          else response.sendRedirect("/login");
                        }))
        .headers(
            headers ->
                headers.contentSecurityPolicy(
                    csp ->
                        csp.policyDirectives(
                            "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src"
                                + " 'self'; object-src 'none'; base-uri 'self'; frame-ancestors"
                                + " 'self'; form-action 'self'")))
        .addFilterBefore(
            new OncePerRequestFilter() {
              @Override
              protected void doFilterInternal(
                  HttpServletRequest request, HttpServletResponse response, FilterChain chain)
                  throws ServletException, IOException {
                var auth = SecurityContextHolder.getContext().getAuthentication();
                if (auth != null && auth.getPrincipal() instanceof Accounts.Principal principal) {
                  if (Accounts.environmentPrincipal(principal)) {
                    chain.doFilter(request, response);
                    return;
                  }
                  var user = db.find(Models.ApplicationUser.class, principal.id());
                  if (user.isEmpty()
                      || !"Approved".equals(user.get().accessStatus)
                      || !Objects.equals(principal.stamp(), user.get().securityStamp)) {
                    SecurityContextHolder.clearContext();
                    if (request.getSession(false) != null) request.getSession(false).invalidate();
                    if (request.getRequestURI().startsWith("/api/")) response.sendError(401);
                    else response.sendRedirect("/login?reason=session-timeout");
                    return;
                  }
                  SecurityContextHolder.getContext().setAuthentication(accounts.token(user.get()));
                }
                chain.doFilter(request, response);
              }
            },
            AnonymousAuthenticationFilter.class);
    return http.build();
  }
}
