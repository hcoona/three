namespace WorkflowDelivery.Destinations;

// State of one exact submitted version; Unobserved means no read was possible.
internal enum StoreState { Absent, Received, InReview, Published, Conflict, Unknown, Unobserved }
