# Display name -> (bpmn fragment builder key, needs_host)
# Each entry produces a MINIMAL VALID process containing that element.
# Where an element cannot stand alone (boundary events, compensation handlers,
# event subprocess starts), the builder supplies a valid host so a deployment
# failure means Flowable rejected the ELEMENT, not my fixture.

SUPPORTED = [
 ("Start Event (None)","start_none"),("Signal Start Event","start_signal"),
 ("Timer Start Event","start_timer"),("Intermediate Catch (Timer)","catch_timer"),
 ("End Event (None)","end_none"),("End Event (Terminate)","end_terminate"),
 ("Task (Generic)","task_generic"),("User Task","task_user"),
 ("Script Task","task_script"),("Service Task (Behavior)","task_service"),
 ("Exclusive Gateway (XOR)","gw_exclusive"),("Inclusive Gateway (OR)","gw_inclusive"),
 ("Parallel Gateway (AND)","gw_parallel"),("Sequence Flow","seq_flow"),
]

COMING_SOON = [
 ("Message Start Event","start_message"),("Conditional Start Event","start_conditional"),
 ("Error Start Event","start_error"),("Escalation Start Event","start_escalation"),
 ("Compensation Start Event","start_compensation"),
 ("Intermediate Throw (None)","throw_none"),("Intermediate Throw (Message)","throw_message"),
 ("Intermediate Throw (Signal)","throw_signal"),("Intermediate Throw (Escalation)","throw_escalation"),
 ("Intermediate Throw (Link)","throw_link"),("Intermediate Throw (Compensation)","throw_compensation"),
 ("Intermediate Catch (Message)","catch_message"),("Intermediate Catch (Signal)","catch_signal"),
 ("Intermediate Catch (Conditional)","catch_conditional"),("Intermediate Catch (Link)","catch_link"),
 ("Message Boundary","bnd_message"),("Timer Boundary","bnd_timer"),
 ("Signal Boundary","bnd_signal"),("Conditional Boundary","bnd_conditional"),
 ("Error Boundary","bnd_error"),("Escalation Boundary","bnd_escalation"),
 ("Cancel Boundary","bnd_cancel"),("Compensation Boundary","bnd_compensation"),
 ("Message End","end_message"),("Signal End","end_signal"),("Error End","end_error"),
 ("Escalation End","end_escalation"),("Cancel End","end_cancel"),("Compensation End","end_compensation"),
 ("Send Task","task_send"),("Receive Task","task_receive"),("Manual Task","task_manual"),
 ("Business Rule Task","task_businessrule"),("Call Activity","task_call"),
 ("Sub-Process (Embedded)","sub_embedded"),("Event Sub-Process","sub_event"),
 ("Transaction","sub_transaction"),("Ad-Hoc Sub-Process","sub_adhoc"),
 ("Event-Based Gateway","gw_eventbased"),("Complex Gateway","gw_complex"),
 ("Loop Marker","mk_loop"),("Multi-Instance (Parallel)","mk_mi_parallel"),
 ("Multi-Instance (Sequential)","mk_mi_sequential"),("Compensation Marker","mk_compensation"),
 ("Pool / Participant","col_pool"),("Lane","col_lane"),("Message Flow","col_messageflow"),
 ("Data Object Reference","data_object"),("Data Store Reference","data_store"),
 ("Data Input","data_input"),("Data Output","data_output"),
 ("Text Annotation","art_annotation"),("Group","art_group"),("Association","art_association"),
]
