﻿using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing.Template;
using System.Text.Encodings.Web;
using Microsoft.Extensions.ObjectPool;
using Microsoft.AspNetCore.Routing.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing.Subdomain.Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Routing
{
    public class SubDomainRoute : Route
    {
        public string[] Hostnames { get; private set; }

        public string Subdomain { get; private set; }

        public SubDomainRoute(string[] hostnames, string subdomain, IRouter target, string routeTemplate, IInlineConstraintResolver inlineConstraintResolver)
        : base(target, routeTemplate, inlineConstraintResolver)
        {
            Hostnames = hostnames;
            Subdomain = subdomain;
        }

        public SubDomainRoute(string[] hostnames, string subdomain, IRouter target, string routeTemplate, RouteValueDictionary defaults, IDictionary<string, object> constraints,
            RouteValueDictionary dataTokens, IInlineConstraintResolver inlineConstraintResolver)
            : base(target, routeTemplate, defaults, constraints, dataTokens, inlineConstraintResolver)
        {
            Hostnames = hostnames;
            Subdomain = subdomain;
        }

        public SubDomainRoute(string[] hostnames, string subdomain, IRouter target, string routeName, string routeTemplate, RouteValueDictionary defaults, IDictionary<string, object> constraints,
           RouteValueDictionary dataTokens, IInlineConstraintResolver inlineConstraintResolver)
           : base(target, routeName, routeTemplate, defaults, constraints, dataTokens, inlineConstraintResolver)
        {
            Hostnames = hostnames;
            Subdomain = subdomain;
        }

        //public SubDomainRoute(string subdomain, string url, RouteValueDictionary defaults, RouteValueDictionary constraints, IRouteHandler routeHandler)
        //    : base(url, defaults, constraints, routeHandler) { Subdomain = subdomain; }
        //

        //public SubDomainRoute(string subdomain, string url, RouteValueDictionary defaults, RouteValueDictionary constraints, 
        //    RouteValueDictionary dataTokens, IRouteHandler routeHandler)
        //    : base(url, defaults, constraints, dataTokens, routeHandler) { Subdomain = subdomain; }

        public override Task RouteAsync(RouteContext context)
        {
            var host = context.HttpContext.Request.Host.Host;

            string foundHostname = GetHostname(host);


            if (foundHostname == null)
                return Task.CompletedTask;

            var subdomain = host.Substring(0, host.IndexOf(GetHostname(host)) - 1);

            return base.RouteAsync(context);
        }

        protected override Task OnRouteMatched(RouteContext context)
        {
            var host = context.HttpContext.Request.Host.Host;
            var subdomain = host.Substring(0, host.IndexOf(GetHostname(host)) - 1);
            var routeData = new RouteData(context.RouteData);

            // this will allow to get value from example view via RouteData
            if (IsParameterName(Subdomain))
            {
                routeData.Values.Add(ParameterNameFrom(Subdomain), subdomain);
            }

            context.RouteData = routeData;

            return base.OnRouteMatched(context);
        }

        public override VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            if(!(context is SubdomainVirtualPathContext))
            {
                return null;
            }

            var subdomainParameter = IsParameterName(Subdomain) ? ParameterNameFrom(Subdomain) : Subdomain;

            bool containsSubdomainParameter = context.Values.ContainsKey(subdomainParameter);

            if (containsSubdomainParameter)
            {
                return ParameterSubdomain(context, subdomainParameter);
            }
            else
            {
                if (!IsParameterName(Subdomain))
                {
                    //todo: there is a problem if more then one static subdomain is defined because if it is then the first one will be matched.
                    //var binder = Binder(context.HttpContext);
                    //var values = binder.GetValues(context.AmbientValues, context.Values);
                    //if(values == null)
                    //{
                    //    return null;
                    //}
                    //var path = binder.BindValues(values.AcceptedValues);
                    //return null;
                    return StaticSubdomain(context, subdomainParameter);
                }
            }

            return null;
        }

        private string GetHostname(string host)
        {
            foreach (var hostname in Hostnames)
            {
                if (!host.EndsWith(hostname) || host == hostname)
                {
                    continue;
                }

                return hostname;
            }

            return null;
        }

        private string ParameterNameFrom(string value)
        {
            return value.Substring(1, value.LastIndexOf("}") - 1);
        }

        private bool IsParameterName(string value)
        {
            if (value.StartsWith("{") && value.EndsWith("}"))
                return true;

            return false;
        }

        private bool EqualsToUrlParameter(string value, string urlParameter)
        {
            var param = ParameterNameFrom(urlParameter);

            return value.Equals(param);
        }

        private string CreateVirtualPathString(VirtualPathData vpd, RouteValueDictionary values)
        {
            var vp = vpd.VirtualPath;

            if (vp.Contains('?'))
            {
                return string.Format("{0}&{1}={2}", vp, Subdomain, values[Subdomain]);
            }
            else
            {
                return string.Format("{0}?{1}={2}", vp, Subdomain, values[Subdomain]);
            }
        }

        private AbsolutPathData StaticSubdomain(VirtualPathContext context, string subdomainParameter)
        {
            var hostBuilder = BuildUrl(context, subdomainParameter);

            var path = base.GetVirtualPath(new VirtualPathContext(context.HttpContext, context.AmbientValues, context.Values));

            if (path == null) { return null; }

            return new AbsolutPathData(this, path.VirtualPath, hostBuilder.ToString());
        }

        private AbsolutPathData ParameterSubdomain(VirtualPathContext context, string subdomainParameter)
        {
            var hostBuilder = BuildUrl(context, context.Values[subdomainParameter].ToString());

            //we have to remove our subdomain so it will not be added as query string while using GetVirtualPath method
            var values = new RouteValueDictionary(context.Values);
            values.Remove(ParameterNameFrom(Subdomain));

            var path = base.GetVirtualPath(new VirtualPathContext(context.HttpContext, context.AmbientValues, values));

            if (path == null) { return null; }

            return new AbsolutPathData(this, path.VirtualPath, hostBuilder.ToString());
        }

        private StringBuilder BuildUrl(VirtualPathContext context, string subdomainValue)
        {
            var hostBuilder = new StringBuilder();
            hostBuilder
                .Append(context.HttpContext.Request.Scheme)
                .Append("://")
                .Append(subdomainValue)
                .Append(".")
                .Append(context.HttpContext.Request.Host);

            return hostBuilder;
        }

        private TemplateBinder Binder(HttpContext context)
        {
            //that's from RouteBase.cs
            var urlEncoder = context.RequestServices.GetRequiredService<UrlEncoder>();
            var pool = context.RequestServices.GetRequiredService<ObjectPool<UriBuildingContext>>();
            return new TemplateBinder(urlEncoder, pool, ParsedTemplate, Defaults);
        }
    }
}
