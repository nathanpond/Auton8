import Modeler from "bpmn-js/lib/Modeler";
import Viewer from "bpmn-js/lib/Viewer";
import NavigatedViewer from "bpmn-js/lib/NavigatedViewer";
import { CreateAppendAnythingModule } from "bpmn-js-create-append-anything";

class AutoNateBpmnModeler extends Modeler {
  constructor(options = {}) {
    const additionalModules = Array.isArray(options.additionalModules) ? options.additionalModules : [];
    super({
      ...options,
      additionalModules: [...additionalModules, CreateAppendAnythingModule]
    });
  }
}

AutoNateBpmnModeler.Viewer = Viewer;
AutoNateBpmnModeler.NavigatedViewer = NavigatedViewer;

if (typeof window !== "undefined") {
  window.BpmnJS = AutoNateBpmnModeler;
}

export default AutoNateBpmnModeler;
