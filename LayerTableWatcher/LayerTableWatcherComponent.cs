﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Components;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;


// RW: No per component watcher? Single event handler? Ala Ladybug's buzzzzz? Output Debug triggers? How does it deal with worksessions? Toggle for linked worksesh docs...

// In order to load the result of this wizard, you will also need to
// add the output bin/ folder of this project to the list of loaded
// folder in Grasshopper.
// You can use the _GrasshopperDeveloperSettings Rhino command for that.

namespace LayerTableEvents
{
    public class LayerTableWatcherComponent : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public LayerTableWatcherComponent()
          : base("LayerTableWatcher", "LayerTableWatcher",
              "Gets the list of layers in the active document per specified events",
              "KieranTimberlake", "Document Info")
        {
            m_new_document = null;
            m_end_open_document = null;
            m_close_document = null;
            m_active_document_changed = null;
            m_layer_table_event = null;
            m_idle = null;

            m_update = false; // how does this act as a true toggle (only on true)?
            m_autoUpdate = false;
            m_eventAdded = true;
            m_eventDeleted = true;
            m_eventModified = true;
            m_eventSorted = false;
            m_eventCurrent = false;
            m_eventModifiedLocked = true;
            m_eventModifiedVisible = true;
            m_eventModifiedParent = true;
            m_eventModifiedName = true;
            m_eventModifiedColor = true;
        }

        private bool m_update;
        private bool m_autoUpdate;
        private bool m_eventAdded;
        private bool m_eventDeleted;
        private bool m_eventModified;
        private bool m_eventSorted;
        private bool m_eventCurrent;
        private bool m_eventModifiedLocked;
        private bool m_eventModifiedVisible;
        private bool m_eventModifiedParent;
        private bool m_eventModifiedName;
        private bool m_eventModifiedColor;

        private EventHandler<DocumentEventArgs> m_new_document;
        private EventHandler<DocumentOpenEventArgs> m_end_open_document;
        private EventHandler<DocumentEventArgs> m_close_document;
        private EventHandler<DocumentEventArgs> m_active_document_changed;
        private EventHandler<LayerTableEventArgs> m_layer_table_event;
        private EventHandler m_idle;

        /// <summary>
        /// Event type
        /// </summary>
        enum EventType
        {
            None,
            RhinoDocEvent,
            LayerTableEvent,
        }
        private EventType m_event_type;

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Update", "U", "Set this value to true to update the layer table.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Auto-Update", "AU", "If this value is set to true, the component will listen for changes to the layer table based on the toggled events, and automatically update each time something changes. Use with caution - you can create an infinite loop if you create layers downstream based on outputs from this component.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Added", "EA", "Trigger on Added event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Deleted", "ED", "Trigger on Deleted event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Modified", "EM", "Trigger on Modified event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Sorted", "ES", "Trigger on Sorted event.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Current", "EC", "Trigger on Current layer change event.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Modified Locked", "EML", "Trigger on Modified:Locked change event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Modified Visible", "EMV", "Trigger on Modified:Visible change event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Modified Parent", "EMP", "Trigger on Modified:Parent change event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Modified Name", "EMN", "Trigger on Modified:Name change event.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Modified Color", "EMC", "Trigger on Modified:Color change event.", GH_ParamAccess.item, true);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Layers", "L", "The list of layer names in the active document.", GH_ParamAccess.list);
            pManager.AddTextParameter("Full Layer Paths", "LF", "The list of layer names in the document, including nesting information.", GH_ParamAccess.list);
            pManager.AddColourParameter("Layer Colors", "C", "The colors of the document Layers.", GH_ParamAccess.list);
            pManager.AddTextParameter("Linetypes", "LT", "The list of linetypes associated with the document layers.", GH_ParamAccess.list);
            pManager.AddTextParameter("Material Names", "M", "The list of material names associated with the document layers.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Visible", "V", "True if layer is visible.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Print Width", "PW", "The print widths associated with the document layers.", GH_ParamAccess.list);
            pManager.AddColourParameter("Print Color", "PC", "The print color of the layer.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Locked", "LL", "True if layer is locked.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Expanded", "LE", "True if layer is expanded.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            UnhookRhinoEvents();

            if (!DA.GetData(0, ref m_update)) return;
            if (!DA.GetData(1, ref m_autoUpdate)) return;
            if (!DA.GetData(2, ref m_eventAdded)) return;
            if (!DA.GetData(3, ref m_eventDeleted)) return;
            if (!DA.GetData(4, ref m_eventModified)) return;
            if (!DA.GetData(5, ref m_eventSorted)) return;
            if (!DA.GetData(6, ref m_eventCurrent)) return;
            if (!DA.GetData(7, ref m_eventModifiedLocked)) return;
            if (!DA.GetData(8, ref m_eventModifiedVisible)) return;
            if (!DA.GetData(9, ref m_eventModifiedParent)) return;
            if (!DA.GetData(10, ref m_eventModifiedName)) return;
            if (!DA.GetData(11, ref m_eventModifiedColor)) return;

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            LayerTable layerTable = doc.Layers;
            LinetypeTable linetypeTable = doc.Linetypes;

            var name = new List<string>();
            var fullPath = new List<string>();
            var color = new List<Color>();
            var linetype = new List<string>();
            var material = new List<string>();
            var visible = new List<bool>();
            var printwidth = new List<double>();
            var printcolor = new List<Color>();
            var locked = new List<bool>();
            var expanded = new List<bool>();

            foreach (Layer layer in layerTable)
            {
                if (!layer.IsDeleted)
                {
                    name.Add(layer.Name);
                    fullPath.Add(layer.FullPath);
                    color.Add(layer.Color);
                    linetype.Add(linetypeTable.FindIndex(layer.LinetypeIndex).Name);
                    material.Add(layer.RenderMaterial?.Name);
                    visible.Add(layer.IsVisible);
                    printwidth.Add(layer.PlotWeight);
                    printcolor.Add(layer.PlotColor);
                    locked.Add(layer.IsLocked);
                    expanded.Add(layer.IsExpanded);
                }
            }

            DA.SetDataList(0, name);
            DA.SetDataList(1, fullPath);
            DA.SetDataList(2, color);
            DA.SetDataList(3, linetype);
            DA.SetDataList(4, material);
            DA.SetDataList(5, visible);
            DA.SetDataList(6, printwidth);
            DA.SetDataList(7, printcolor);
            DA.SetDataList(8, locked);
            DA.SetDataList(9, expanded);

            if (m_autoUpdate) HookRhinoEvents();
        }

        private void HookRhinoEvents()
        {
            if (null == m_new_document)
                RhinoDoc.NewDocument += m_new_document = OnRhinoDocEvent;

            if (null == m_end_open_document)
                RhinoDoc.EndOpenDocument += m_end_open_document = OnRhinoDocEvent;

            if (null == m_close_document)
                RhinoDoc.CloseDocument += m_close_document = OnRhinoDocEvent;

            if (null == m_active_document_changed)
                RhinoDoc.ActiveDocumentChanged += m_active_document_changed = OnRhinoDocEvent;

            if (null == m_layer_table_event)
                RhinoDoc.LayerTableEvent += m_layer_table_event = OnLayerTableEvent;
        }

        private void UnhookRhinoEvents()
        {
            if (null != m_new_document)
            {
                RhinoDoc.NewDocument -= m_new_document;
                m_new_document = null;
            }

            if (null != m_end_open_document)
            {
                RhinoDoc.EndOpenDocument -= m_end_open_document;
                m_end_open_document = null;
            }

            if (null != m_close_document)
            {
                RhinoDoc.CloseDocument -= m_close_document;
                m_close_document = null;
            }

            if (null != m_active_document_changed)
            {
                RhinoDoc.ActiveDocumentChanged -= m_active_document_changed;
                m_active_document_changed = null;
            }

            if (null != m_layer_table_event)
            {
                RhinoDoc.LayerTableEvent -= m_layer_table_event;
                m_layer_table_event = null;
            }
        }

        private void HookRhinoIdle(EventType eventType)
        {
            if (null == m_idle)
            {
                m_event_type = eventType;
                RhinoApp.Idle += m_idle = OnIdle;
            }
        }

        private void UnhookRhinoIdle()
        {
            m_event_type = EventType.None;
            if (null != m_idle)
            {
                RhinoApp.Idle -= m_idle;
                m_idle = null;
            }
        }

        private void OnRhinoDocEvent(object sender, DocumentEventArgs args)
        {
            HookRhinoIdle(EventType.RhinoDocEvent);
        }

        private void OnLayerTableEvent(object sender, LayerTableEventArgs args)
        {
            if (LayerEventShouldExpire(args)) HookRhinoIdle(EventType.LayerTableEvent);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            RhinoApp.WriteLine("DocumentContextChanged");
            switch (context)
            {
                case GH_DocumentContext.Open:
                    RhinoApp.WriteLine("Open");
                    break;
                case GH_DocumentContext.Close:
                    RhinoApp.WriteLine("Close");
                    UnhookRhinoEvents();
                    break;
                case GH_DocumentContext.Loaded:
                    RhinoApp.WriteLine("Loaded");
                    if (m_autoUpdate) ExpireSolution(true);
                    break;
                case GH_DocumentContext.Unloaded:
                    RhinoApp.WriteLine("Unloaded");
                    UnhookRhinoEvents();
                    break;
                case GH_DocumentContext.Lock:
                    RhinoApp.WriteLine("Lock");
                    UnhookRhinoEvents();
                    break;
                case GH_DocumentContext.Unlock:
                    RhinoApp.WriteLine("Unlock");
                    if (m_autoUpdate) ExpireSolution(true);
                    break;
                case GH_DocumentContext.None:
                    RhinoApp.WriteLine("None");
                    break;
                case GH_DocumentContext.Unknown:
                    RhinoApp.WriteLine("Unknown");
                    break;
                default:
                    break;
            }
        }

        public override void AddedToDocument(GH_Document document)
        {
            RhinoApp.WriteLine("AddedToDocument");
            if (Locked)
            {
                RhinoApp.WriteLine("Locked");
                UnhookRhinoEvents();
            }
        }
        
        public override void RemovedFromDocument(GH_Document document)
        {
            RhinoApp.WriteLine("RemovedFromDocument");
            UnhookRhinoEvents();
        }


        private void OnIdle(object sender, EventArgs args)
        {
            RhinoApp.WriteLine("OnIdle");
            switch (m_event_type)
            {
                case EventType.RhinoDocEvent:
                    RhinoApp.WriteLine("RhinoDocEvent");
                    ExpireSolution(true);
                    break;
                case EventType.LayerTableEvent:
                    RhinoApp.WriteLine("LayerTableEvent");
                    ExpireSolution(true);
                    break;
            }
            UnhookRhinoIdle();
        }

         private bool LayerEventShouldExpire(LayerTableEventArgs args)
         {
            RhinoApp.WriteLine("ProcessLayerTableEvent");
            switch (args.EventType)
            {
                case LayerTableEventType.Added:
                    RhinoApp.WriteLine("Added");
                    return m_eventAdded;
                case LayerTableEventType.Deleted:
                    RhinoApp.WriteLine("Deleted");
                    return m_eventDeleted;
                case LayerTableEventType.Undeleted:
                    RhinoApp.WriteLine("Undeleted");
                    return m_eventAdded;
                case LayerTableEventType.Sorted:
                    RhinoApp.WriteLine("Sorted");
                    return m_eventSorted;
                case LayerTableEventType.Current:
                    RhinoApp.WriteLine("Current");
                    return m_eventCurrent;
                case LayerTableEventType.Modified:
                    RhinoApp.WriteLine("Modified");
                    if (m_eventModified)
                    {
                        if (!m_eventModifiedLocked || 
                            !m_eventModifiedVisible ||
                            !m_eventModifiedParent || 
                            !m_eventModifiedName ||
                            !m_eventModifiedColor)
                        {
                            RhinoApp.WriteLine("Modifier type is toggled off");
                            if (args.OldState != null && args.NewState != null)
                            {
                                return ((m_eventModifiedLocked && args.OldState.IsLocked != args.NewState.IsLocked) ||
                                    (m_eventModifiedVisible && args.OldState.IsVisible != args.NewState.IsVisible) ||
                                    (m_eventModifiedParent && args.OldState.ParentLayerId != args.NewState.ParentLayerId) ||
                                    (m_eventModifiedName && args.OldState.Name != args.NewState.Name) ||
                                    (m_eventModifiedColor && args.OldState.Color != args.NewState.Color));
                            }
                        }
                    }
                    return m_eventModified;
                default:
                    return true;
             }
         }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("30954f8f-bb74-4705-9c6a-50f1d672832e"); }
        }
    }
}
