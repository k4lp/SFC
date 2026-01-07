# Infrastructure Processing Directory Explanation

## 1. Overview
The `src/SalesforceCore/Infrastructure/Processing` directory contains the logic for **asynchronous batch processing**. This system allows the application to queue items (like audit logs or simple data updates) and process them in efficient batches in the background, rather than executing one API call per item.

## 2. Key Components

### `ChannelBatchProcessor`
**Purpose**: The core engine. It wraps a `Channel<T>` and a background loop.
**Logic**:
1. Producers write items to the Channel.
2. A background `Task` reads from the Channel.
3. It accumulates items until a **batch size** is reached OR a **time window** elapses.
4. It then dispatches the batch to a handler.

### `IBatchProcessor`
**Purpose**: Interface for the processor, allowing it to be mocked or swapped.

### `IChannelBatchHandler`
**Purpose**: The interface that actually *does* the work (e.g., calls the Salesforce API) given a list of items.

## 3. C# Concepts
- **`System.Threading.Channels`**: High-performance, thread-safe queues designed for producer-consumer scenarios. They support "Bounded" capacity (backpressure) to prevent memory overflows.
- **Background Tasks**: The processor likely runs as a `IHostedService` or starts a long-running Task to process the queue independently of HTTP requests.
