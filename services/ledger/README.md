# ledger

C# / .NET service that records money. Every posting is a balanced set of
double-entry lines; entries are insert-only and corrections are reversing
transactions. Serves balances and statements.

Owns the `ledger` schema in PostgreSQL.
