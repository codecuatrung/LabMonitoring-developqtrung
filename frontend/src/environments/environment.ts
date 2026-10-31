export const environment = {
  production: false,

  /** Lab Monitoring API base (proxied in dev via proxy.conf.json). */
  apiBaseUrl: '/api/v1',

  /** SignalR hub — absolute so the WebSocket bypasses the dev proxy. */
  hubUrl: 'http://localhost:5200/hubs/lab',

  keycloak: {
    issuer: 'https://sso2.huph.edu.vn/realms/coreproject',
    clientId: 'labmonitoring-spa',
  },
};
