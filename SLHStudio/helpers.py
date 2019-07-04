import inspect
import clr
clr.AddReference('System.Reflection')
import System.Reflection

class CLRInfo():
    def __init__(self, fields, methods, properties, events):
        self.fields = fields
        self.methods = methods
        self.properties = properties
        self.events = events

    @classmethod
    def _is_event(cls, name):
        return name.startswith('add_') or name.startswith('remove_')

    @classmethod
    def _is_property(cls, name):
        return name.startswith('get_') or name.startswith('set_')

    @classmethod
    def get_type_info(cls, typ):
        class_attrs = inspect.classify_class_attrs(typ)
        things = [(name, callable(value), cls._is_property(name),
                   cls._is_event(name))
                  for name, kind, _, value in class_attrs
                  if kind == 'data' and name[0] != '_' and name[-2:] != '__']
        fields = {name for name, c, p, e in things if not c and not (e or p)}
        methods = {name for name, c, p, e in things if c and not (e or p)}
        properties = {name[4:] for name, _, p, _ in things if
                      p and name.startswith('get_')}
        # Filter out auto-property fields
        fields -= properties
        events = {name[4:] for name, _, _, e in things if
                  e and name.startswith('add_')}
        return CLRInfo(fields, methods, properties, events)

    @classmethod
    def print_type_info(cls, typ):
        info = cls.get_type_info(typ)
        lines = []
        for key, value in iter(info):
            lines.append(key)
            for v in value:
                lines.append(f'  - {v}')
        print('\n'.join(lines))

    def __iter__(self):
        for key in ['fields', 'methods', 'properties', 'events']:
            yield key, getattr(self, key)

# Helper functions :)
def remove_all_event_handlers(declaring_object, event_name, field_name=None):
    """
    Removes all event handlers of an event on its declaring object
    """
    field_name = field_name or event_name
    flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
    typ = declaring_object.GetType()
    evt = typ.GetEvent(event_name)
    field = typ.GetField(field_name, flags) or typ.GetField('m_' + field_name,
                                                            flags)
    value = field.GetValue(declaring_object)
    if value:
        for delegate in value.GetInvocationList():
            evt.RemoveEventHandler(declaring_object, delegate)