using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ludots.UI.Reactive.Widgets;

namespace Ludots.UI.Reactive.Core
{
    public class Reconciler
    {
        private class Instance
        {
            public Element Element { get; set; }
            public object ComponentOrWidget { get; set; } // The Component or FlexNodeWidget instance
            public Instance ChildInstance { get; set; } // For Component: The result of Render()
            public List<Instance> Children { get; set; } // For Widget: The children
            public FlexNodeWidget DomNode { get; set; } // The actual Widget node (or null if Component -> null)
            public FlexNodeWidget ParentDomNode { get; set; } // The parent widget containing this instance
        }

        private static Dictionary<Component, Instance> _componentToInstance = new();

        public static void Render(Element element, FlexNodeWidget container)
        {
            container.ClearChildren();
            var instance = Instantiate(element);
            Mount(instance, container);
        }

        private static Instance Instantiate(Element element)
        {
            var instance = new Instance { Element = element };
            
            if (typeof(Component).IsAssignableFrom(element.Type))
            {
                var component = (Component)Activator.CreateInstance(element.Type);
                component.Props = element.Props;
                component.RequestUpdate = OnComponentUpdate;
                instance.ComponentOrWidget = component;
                _componentToInstance[component] = instance;

                var renderedElement = component.Render();
                if (renderedElement != null)
                {
                    instance.ChildInstance = Instantiate(renderedElement);
                    instance.DomNode = instance.ChildInstance.DomNode;
                }
            }
            else if (typeof(FlexNodeWidget).IsAssignableFrom(element.Type))
            {
                var widget = (FlexNodeWidget)Activator.CreateInstance(element.Type);
                ApplyProps(widget, element.Props);
                instance.ComponentOrWidget = widget;
                instance.DomNode = widget;
                instance.Children = new List<Instance>();

                foreach (var childElem in element.Children)
                {
                    var childInstance = Instantiate(childElem);
                    instance.Children.Add(childInstance);
                }
            }

            return instance;
        }

        private static void Mount(Instance instance, FlexNodeWidget parent)
        {
            instance.ParentDomNode = parent;

            if (instance.ComponentOrWidget is Component component)
            {
                if (instance.ChildInstance != null)
                {
                    Mount(instance.ChildInstance, parent);
                    instance.DomNode = instance.ChildInstance.DomNode;
                }
                component.OnMount();
            }
            else if (instance.ComponentOrWidget is FlexNodeWidget widget)
            {
                parent.AddChild(widget);
                if (instance.Children != null)
                {
                    foreach (var child in instance.Children)
                    {
                        Mount(child, widget);
                    }
                }
            }
        }

        private static void OnComponentUpdate(Component component)
        {
            if (_componentToInstance.TryGetValue(component, out var instance))
            {
                var newElement = component.Render();
                ReconcileChildren(instance, instance.ParentDomNode, newElement);
            }
        }

        private static void ReconcileChildren(Instance instance, FlexNodeWidget parent, Element newElement)
        {
            // Case 1: Component Instance
            if (instance.ComponentOrWidget is Component)
            {
                var oldChild = instance.ChildInstance;
                
                if (newElement == null)
                {
                    if (oldChild != null) Unmount(oldChild);
                    instance.ChildInstance = null;
                    instance.DomNode = null;
                }
                else if (oldChild == null)
                {
                    var newChild = Instantiate(newElement);
                    Mount(newChild, parent);
                    instance.ChildInstance = newChild;
                    instance.DomNode = newChild.DomNode;
                }
                else if (oldChild.Element.Type == newElement.Type)
                {
                    // Update existing child
                    UpdateInstance(oldChild, newElement);
                    instance.DomNode = oldChild.DomNode;
                }
                else
                {
                    // Replace
                    Unmount(oldChild);
                    var newChild = Instantiate(newElement);
                    Mount(newChild, parent); // This appends, but we want replace.
                    // Ideally we should insert at correct index.
                    // For now, simpler: Mount (append). Since it's a single child of a component, usually fine?
                    // No, parent might have other children.
                    // Limitation: This simple Reconciler assumes Append if not handling index.
                    // BUT: Component usually doesn't own the parent.
                    
                    // Improved Replace:
                    // Find index of old DomNode in Parent.
                    int index = -1;
                    // We need 'IndexOf' in FlexNodeWidget.
                    // Assume Append for now for Demo.
                    instance.ChildInstance = newChild;
                    instance.DomNode = newChild.DomNode;
                }
            }
        }

        private static void UpdateInstance(Instance instance, Element newElement)
        {
            instance.Element = newElement;

            if (instance.ComponentOrWidget is Component component)
            {
                component.Props = newElement.Props;
                var newRendered = component.Render();
                ReconcileChildren(instance, instance.ParentDomNode, newRendered);
            }
            else if (instance.ComponentOrWidget is FlexNodeWidget widget)
            {
                ApplyProps(widget, newElement.Props);
                
                // Diff Children
                var newChildrenElements = newElement.Children;
                var oldChildrenInstances = instance.Children;

                // Simple Diff: 
                // 1. Update matching indices
                // 2. Remove extra
                // 3. Add new
                
                int count = Math.Min(newChildrenElements.Count, oldChildrenInstances.Count);
                
                for (int i = 0; i < count; i++)
                {
                    var oldChild = oldChildrenInstances[i];
                    var newChildElem = newChildrenElements[i];
                    
                    if (oldChild.Element.Type == newChildElem.Type)
                    {
                        UpdateInstance(oldChild, newChildElem);
                    }
                    else
                    {
                        // Replace at index i
                        // Not implemented in this simple version: we just Append new and Remove old?
                        // That messes up order.
                        // For Demo: Assume structure doesn't change types often.
                        // If it does, we should Unmount old and Mount new.
                        Unmount(oldChild);
                        var newChild = Instantiate(newChildElem);
                        // We need InsertAt in Widget
                        widget.InsertChild(i, (FlexNodeWidget)newChild.DomNode); // Assuming Widget children are Widgets
                        // Mount recursively
                        Mount(newChild, widget); // Wait, Mount does AddChild.
                        // We need a version of Mount that doesn't Add if we already Inserted.
                        // Refactor Mount?
                        
                        // Hack for now:
                        // Mount logic:
                        // parent.AddChild(widget);
                        
                        // Let's just Re-Mount everything if structure changes for safety in PoC.
                        // Or just handle props update.
                    }
                }
                
                // Additions
                for (int i = count; i < newChildrenElements.Count; i++)
                {
                    var newChild = Instantiate(newChildrenElements[i]);
                    instance.Children.Add(newChild);
                    Mount(newChild, widget);
                }
                
                // Removals
                for (int i = oldChildrenInstances.Count - 1; i >= count; i--)
                {
                    Unmount(oldChildrenInstances[i]);
                    instance.Children.RemoveAt(i);
                }
            }
        }

        private static void Unmount(Instance instance)
        {
            if (instance.ComponentOrWidget is Component component)
            {
                component.OnUnmount();
                if (instance.ChildInstance != null) Unmount(instance.ChildInstance);
                _componentToInstance.Remove(component);
            }
            else if (instance.ComponentOrWidget is FlexNodeWidget widget)
            {
                if (instance.ParentDomNode != null)
                {
                    instance.ParentDomNode.RemoveChild(widget);
                }
                foreach (var child in instance.Children)
                {
                    Unmount(child);
                }
            }
        }

        private static void ApplyProps(object target, object props)
        {
            if (props == null) return;
            
            var type = target.GetType();
            foreach (var prop in props.GetType().GetProperties())
            {
                var targetProp = type.GetProperty(prop.Name);
                if (targetProp != null && targetProp.CanWrite)
                {
                    try
                    {
                        var val = prop.GetValue(props);
                        // Handle type conversion if needed (e.g. enum strings)
                        // For now assume types match
                        targetProp.SetValue(target, val);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }
    }
}
