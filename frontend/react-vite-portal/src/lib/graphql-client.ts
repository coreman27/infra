import { GraphQLClient } from 'graphql-request';
import { auth } from './firebase';

const HASURA_ENDPOINT = import.meta.env.VITE_HASURA_ENDPOINT;

// Create authenticated GraphQL client
export const graphqlClient = new GraphQLClient(HASURA_ENDPOINT, {
  requestMiddleware: async (request) => {
    const user = auth.currentUser;
    if (user) {
      const token = await user.getIdToken();
      return {
        ...request,
        headers: {
          ...request.headers,
          Authorization: `Bearer ${token}`
        }
      };
    }
    return request;
  }
});
