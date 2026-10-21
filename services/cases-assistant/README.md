# cases-assistant

Python service that opens a review case for every held payment and records
the analyst's decision. Includes an AI assistant that drafts case summaries
from retrieved records, with citations. The assistant is read-only: it can
never approve, decline or move money.

Owns the `cases` schema in PostgreSQL.
