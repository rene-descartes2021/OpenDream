using System.Reflection;
using System.Text;
using OpenDreamRuntime.Objects;
using OpenDreamShared.Dream;
using OpenDreamShared.Dream.Procs;

namespace OpenDreamRuntime.Procs {
    public sealed class NativeProc : DreamProc {
        public delegate DreamValue HandlerFn(DreamObject src, DreamObject usr, DreamProcArguments arguments);

        public static (string, Dictionary<string, DreamValue>, List<String>) GetNativeInfo(Delegate func) {
            List<Attribute> attributes = new(func.GetInvocationList()[0].Method.GetCustomAttributes());
            DreamProcAttribute procAttribute = (DreamProcAttribute)attributes.Find(attribute => attribute is DreamProcAttribute);
            if (procAttribute == null) throw new ArgumentException();

            Dictionary<string, DreamValue> defaultArgumentValues = null;
            var argumentNames = new List<string>();
            List<Attribute> parameterAttributes = attributes.FindAll(attribute => attribute is DreamProcParameterAttribute);
            foreach (Attribute attribute in parameterAttributes) {
                DreamProcParameterAttribute parameterAttribute = (DreamProcParameterAttribute)attribute;

                argumentNames.Add(parameterAttribute.Name);
                if (parameterAttribute.DefaultValue != default) {
                    if (defaultArgumentValues == null) defaultArgumentValues = new Dictionary<string, DreamValue>();

                    defaultArgumentValues.Add(parameterAttribute.Name, new DreamValue(parameterAttribute.DefaultValue));
                }
            }

            return (procAttribute.Name, defaultArgumentValues, argumentNames);
        }

        public sealed class State : ProcState {
            public static readonly Stack<State> Pool = new();

            public DreamObject Src;
            public DreamObject Usr;
            public DreamProcArguments Arguments;

            private NativeProc _proc;
            public override NativeProc Proc => _proc;

            public void Initialize(NativeProc proc, DreamThread thread, DreamObject src, DreamObject usr, DreamProcArguments arguments) {
                base.Initialize(thread, true);

                _proc = proc;
                Src = src;
                Usr = usr;
                Arguments = arguments;
            }

            protected override ProcStatus InternalResume() {
                Result = _proc.Handler.Invoke(Src, Usr, Arguments);

                return ProcStatus.Returned;
            }

            public override void AppendStackFrame(StringBuilder builder) {
                if (_proc == null) {
                    builder.Append("<anonymous proc>");
                    return;
                }

                builder.Append($"{_proc.Name}");
            }

            public override void Dispose() {
                base.Dispose();

                Src = null!;
                Usr = null!;
                Arguments = default;
                _proc = null!;

                Pool.Push(this);
            }
        }

        private Dictionary<string, DreamValue> _defaultArgumentValues;
        public HandlerFn Handler { get; }

        public NativeProc(DreamPath owningType, string name, DreamProc superProc, List<String> argumentNames, List<DMValueType> argumentTypes, Dictionary<string, DreamValue> defaultArgumentValues, HandlerFn handler, string? verbName, string? verbCategory, string? verbDesc, sbyte? invisibility)
            : base(owningType, name, superProc, ProcAttributes.None, argumentNames, argumentTypes, verbName, verbCategory, verbDesc, invisibility) {
            _defaultArgumentValues = defaultArgumentValues;
            Handler = handler;
        }

        public override State CreateState(DreamThread thread, DreamObject src, DreamObject usr, DreamProcArguments arguments) {
            if (_defaultArgumentValues != null) {
                var newNamedArguments = arguments.NamedArguments;
                foreach (KeyValuePair<string, DreamValue> defaultArgumentValue in _defaultArgumentValues) {
                    int argumentIndex = ArgumentNames.IndexOf(defaultArgumentValue.Key);

                    if (arguments.GetArgument(argumentIndex, defaultArgumentValue.Key) == DreamValue.Null) {
                        newNamedArguments ??= new();
                        newNamedArguments.Add(defaultArgumentValue.Key, defaultArgumentValue.Value);
                    }
                }
                arguments = new DreamProcArguments(arguments.OrderedArguments, newNamedArguments);
            }

            if (!State.Pool.TryPop(out var state)) {
                state = new State();
            }

            state.Initialize(this, thread, src, usr, arguments);
            return state;
        }
    }
}
