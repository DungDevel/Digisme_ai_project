## Problem Statement

The current POS API testing infrastructure lacks consistency between test and production environments, particularly in transaction handling and retry configurations. The existing integration tests do not fully verify production retry behavior, and the DatabaseFixture implementation has incomplete resource lifecycle management and database safety protections.

## Solution

Implement a revised testing infrastructure that:
1. Supports both external transaction rollback testing and service-owned transaction verification
2. Aligns retry configurations with production where appropriate
3. Enforces strict database identity protection
4. Implements deterministic resource lifecycle management
5. Preserves critical PostgreSQL locking tests
6. Adds transaction ownership verification capabilities

## User Stories

1. As a developer, I want tests that accurately reflect production retry behavior, so that I can trust test results match production outcomes
2. As a tester, I want to have both external transaction rollback tests and service-owned transaction verification tests, so that I can fully validate transaction handling scenarios
3. As a DevOps engineer, I want strict database identity enforcement, so that tests cannot accidentally target production databases
4. As a QA engineer, I want automatic rollback of uncommitted transactions, so that test data remains clean without destructive cleanup
5. As a maintainer, I want observable migration failures, so that I can identify and fix database schema issues early
6. As a developer, I want to preserve existing test semantics unless there's a confirmed defect, so that I don't introduce unexpected test behavior

## Implementation Decisions

### Infrastructure Improvements
- Implement IClassFixture for PostgreSQL integration-test classes
- Create TestDatabaseSession abstraction for resource ownership
- Enforce exact database name `digism_pos_test` using NpgsqlConnectionStringBuilder
- Implement automatic rollback of unfinished external transactions
- Add exception-safe DisposeAsync implementation
- Make migration failures observable
- Ensure deterministic disposal of context, transaction, and connection
- Avoid automatic ClearData() during fixture disposal

### Transaction Strategy Implementation
- For Strategy A (external transaction):
  - Preserve existing 18 business integration tests
  - Ensure tests create external transactions
  - Verify service reuses Database.CurrentTransaction
  - Ensure tests roll back after assertions
  - Implement deterministic disposal contract

- For Strategy B (service-owned transaction):
  - Add focused tests where justified
  - Ensure service creates and owns its transaction
  - Use production retrying execution strategy
  - Verify transaction creation, commit, and failure handling
  - Implement safe cleanup for committed test data

## Testing Decisions

### Test Coverage
- Preserve real PostgreSQL integration tests for:
  - InventoryService: Products
  - OrderService: Products
  - ReturnService: OrderItems
  - StockInService: SupplierInvoices and Products

### Test Design
- Only test external behavior, not implementation details
- Use existing integration tests as base for Strategy A
- Add new tests for Strategy B where justified
- Ensure tests do not assume DisposeAsync can roll back committed transactions
- Avoid blanket destructive cleanup

## Out of Scope

- No production source code modifications
- No changes to existing test semantics unless confirmed defects
- No Docker configuration changes
- No database connection or modification operations
- No branch switching or version control operations
- No implementation of new business features

## Further Notes

- This specification focuses exclusively on testing infrastructure improvements
- Implementation will follow repository's established documentation conventions
- Transaction ownership model will be strictly enforced
- Database safety requirements will be strictly enforced
- Implementation phases will be carefully planned to avoid disrupting existing test semantics