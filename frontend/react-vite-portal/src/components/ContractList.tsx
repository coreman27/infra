import { useAuth } from '@/hooks/useAuth';
import { gql } from '@/gql';
import { useQuery, useMutation } from '@tanstack/react-query';
import { graphqlClient } from '@/lib/graphql-client';

// GraphQL query (automatically generates types via GraphQL Code Generator)
const GET_CUSTOMER_CONTRACTS = gql(`
  query GetCustomerContracts($customerId: String!) {
    chargebee_contract(
      where: { customer_id: { _eq: $customerId } }
      order_by: { created_at: desc }
    ) {
      id
      status
      start_date
      end_date
      length_months
      auto_renew
      subscriptions(limit: 1) {
        id
        item_price_id
        status
      }
    }
  }
`);

const UPDATE_CONTRACT_AUTO_RENEW = gql(`
  mutation UpdateContractAutoRenew($contractId: String!, $autoRenew: Boolean!) {
    update_chargebee_contract_by_pk(
      pk_columns: { id: $contractId }
      _set: { auto_renew: $autoRenew }
    ) {
      id
      auto_renew
    }
  }
`);

export function ContractList() {
  const { user } = useAuth();

  // Query with auto-generated types
  const { data, isLoading, error } = useQuery({
    queryKey: ['contracts', user?.customerId],
    queryFn: async () => {
      if (!user?.customerId) throw new Error('No customer ID');
      
      return graphqlClient.request(GET_CUSTOMER_CONTRACTS, {
        customerId: user.customerId
      });
    },
    enabled: !!user?.customerId
  });

  // Mutation with auto-generated types
  const updateAutoRenew = useMutation({
    mutationFn: async (variables: { contractId: string; autoRenew: boolean }) => {
      return graphqlClient.request(UPDATE_CONTRACT_AUTO_RENEW, variables);
    },
    onSuccess: () => {
      // Invalidate and refetch
      queryClient.invalidateQueries({ queryKey: ['contracts'] });
    }
  });

  if (isLoading) return <div>Loading contracts...</div>;
  if (error) return <div>Error: {error.message}</div>;

  return (
    <div className="space-y-4">
      <h2 className="text-2xl font-bold">My Contracts</h2>
      {data?.chargebee_contract.map(contract => (
        <div key={contract.id} className="border p-4 rounded">
          <div className="flex justify-between items-start">
            <div>
              <h3 className="font-semibold">Contract {contract.id}</h3>
              <p className="text-sm text-gray-600">
                Status: <span className="font-medium">{contract.status}</span>
              </p>
              <p className="text-sm text-gray-600">
                Duration: {contract.length_months} months
              </p>
              {contract.start_date && (
                <p className="text-sm text-gray-600">
                  Start: {new Date(contract.start_date).toLocaleDateString()}
                </p>
              )}
              {contract.end_date && (
                <p className="text-sm text-gray-600">
                  End: {new Date(contract.end_date).toLocaleDateString()}
                </p>
              )}
            </div>
            
            <label className="flex items-center space-x-2">
              <input
                type="checkbox"
                checked={contract.auto_renew}
                onChange={(e) => updateAutoRenew.mutate({
                  contractId: contract.id,
                  autoRenew: e.target.checked
                })}
                disabled={contract.status !== 'active'}
              />
              <span className="text-sm">Auto-renew</span>
            </label>
          </div>

          {contract.subscriptions[0] && (
            <div className="mt-2 pt-2 border-t">
              <p className="text-xs text-gray-500">
                Subscription: {contract.subscriptions[0].id}
              </p>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
