# personalaffe — Idea Collection

> **Status:** Non-binding idea collection · **Language:** English
>
> The ideas recorded here are not approved product features and are not part
> of the MVP. Each idea requires a separate decision later on whether it fits
> personalaffe and should be implemented. Entries are therefore deliberately
> brief and are not specified in detail at this stage.

## Read Multiple Email Accounts via IMAP

A dedicated area could provide read access to multiple IMAP accounts and a
quick overview of received and new emails.

- A dashboard tile could summarize the number of new emails per account.
- Sending email is explicitly outside the initial scope.
- Whether emails may be deleted from within personalaffe remains open.
- Sending email later would be a separate expansion requiring its own
  evaluation.
- AI agents could access emails authorized for agent access through the CLI
  and later through MCP; access always goes through the personalaffe server.
- For each connected IMAP account, the user separately decides whether agents
  may access it.

## Bookmarks and Reading List

A home for interesting websites, articles, tools, and videos: what do I still
want to look at, and what do I want to find again in the long term? The focus
is on external sources and the progression from "saved" to "read" or "kept."
AI agents could file research findings directly here.

## Journal / Personal Logbook

A chronological home for thoughts, small achievements, decisions, and work
completed, such as a server migration. While Knowledge describes the current
state of understanding, the journal preserves the history and its context
at the time.

## Inventory and Lent Items

An overview of personal devices, tools, accessories, storage locations, and
items lent to others. It answers questions such as "How much RAM does my small
server have?", "Where is the adapter?", or "Who did I lend the drill to?"
Receipts and manuals could be linked to the Files area.

## Contracts, Subscriptions, and Renewal Dates

A compact overview of ongoing commitments such as internet contracts,
streaming subscriptions, insurance, domains, or server rentals: costs,
renewals, and cancellation deadlines become visible in one place. Tasks could
be created from these when needed.

For example, an instructed AI agent could read contract details from an
already signed-in Chrome session on Check24 and enter them into personalaffe
through the CLI. The agent gathers information outside the application;
personalaffe serves as a personal repository and overview.

## Feeds / Personal News Reader

A small RSS/Atom reader for selected blogs, publications, and project updates.
Interesting posts could then be moved into the reading list or knowledge base.

External AI agents could also research or collect news and submit it through
the CLI. An agent may run on another machine; the application does not have
to fetch the content through feeds itself.

## People and Keeping in Touch

A private contact book with personal context: how do I know someone, what did
we last talk about, and when would I like to get in touch again? The focus is
on relationships and encounters. Explicit authorization for agent access
would be particularly useful here.

## Recipes and Meal Planning

Collect personal recipes, choose meals for the next few days, and derive a
shopping list from them. Ingredients, quantities, and planning give this area
a purpose beyond simply storing recipe text.

## Personal Notification Center

Other products such as planaffe or hostingaffe, as well as external agents,
could submit personal notifications to personalaffe as an alternative to email.
Initially, these would appear on the dashboard or within the open web app and
could link back to their source.

personalaffe would become a central inbox and a launch point into other
applications. A future mobile app could build on this and add push
notifications, for example. Domain-specific work remains in the source product.

## Personal Overview of Agent Assignments

An overview could show which assignments external AI agents are currently
working on for me, where a question is awaiting my answer, and where results
are available, even when agents run on different machines. The source and time
of the latest update would help put the reported state in context.

As a possible extension of the notification center, agents or their tools
could submit status updates and references to results through the CLI or API.
Execution remains with the respective agent tools. How to make this integration
reliable and what feedback is possible in the web app remain open and must be
examined before implementation.

## Small Personal Server Overview

A deliberately concise overview answers "Which servers do I actually have?",
for example with a name, provider, personal purpose, and link to the relevant
management or documentation application.

This area would help the user find their bearings. Extensive operations
documentation and server management remain in hostingaffe or other dedicated
products; a future integration could populate the overview with information
from these sources.

## Personal Change Radar

Record personal monitoring requests such as "When this tool gets feature X,"
"when this product is available again," or "when the terms of my plan change."
External agents could check the sources and submit relevant changes in the
context of the original question.

## Decision Workshop

A place for pending personal decisions such as moving home, making a purchase,
or planning a trip. Options, personal criteria, and open questions are
collected; agents could gather missing information. After the decision, its
rationale remains understandable in light of what was known at the time.

## Personal Instruction Book for Agents

Record preferences, requirements, and boundaries once and selectively pass
them to different agents: for example, travel preferences, purchasing
exclusions, preferred output formats, or "Always present bookings for approval
first." This information could be maintained centrally, with relevant portions
provided for each assignment.

## Agent Results Gallery

A visible home for comparison tables, small websites, diagrams, reports, and
drafts created by agents. Previews, purpose, and provenance help users find
results again and browse past work. This idea would deliberately go beyond the
currently simple file storage.

## "Can This Go?" Cleanup Station

Agents could collect cleanup suggestions for duplicate files, outdated
information, neglected reading lists, or contradictory entries. The user is
presented with small, reasoned decisions: keep, merge, archive, or delete.
Maintaining personal content would have a dedicated place where suggestions
can be reviewed before they are applied.

## Private Time Capsule

Save questions, predictions, messages, or a project's current state for your
future self and have them resurface at a chosen time. For example, the
expectation "I will use this tool every day" could return after six months as a
reminder of the original assessment and an invitation to compare it with what
actually happened.

## Custom Financial Ticker on the Dashboard

A small dashboard tile shows prices for selected stocks, ETFs, and currency
pairs. The user chooses which instruments to follow. The data source and
refresh interval remain open; the timestamp of the quotes should be visible.

## Standardized Data Submission for Personal Overviews

Custom web apps, scripts, or external agents could submit data to personalaffe
through an authenticated interface. This data could populate statistics tiles
on the dashboard or separate pages with suitable views. The user could then
see information from their own applications in one shared place.

A small set of three or four fixed data formats with predefined visualizations
could be sufficient. Possible formats include:

- **Metrics:** named keys with values and optional units, such as a count or
  current measurement.
- **Time series:** continuously appended timestamped values displayed as a
  trend over time.
- **Categories with values:** for example, counts per category shown as a
  breakdown or bar comparison.
- **Status updates:** a named state with a timestamp and optional short text
  for a compact status overview.

The specific formats and their mapping to views remain open. The idea relies
on standardized data and visualizations; a freely programmable page or
workflow builder is not intended.
